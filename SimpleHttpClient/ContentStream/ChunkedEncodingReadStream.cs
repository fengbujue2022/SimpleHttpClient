using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class ChunkedEncodingReadStream : HttpContentReadStream
    {
        private const int MaxChunkBytesAllowed = 16 * 1024;
        private const int MaxTrailingHeaderLength = 16 * 1024;
        private ulong _chunkBytesRemaining;
        private ParsingState _state = ParsingState.ExpectChunkHeader;
        private HttpResponseMessage _response;

        internal ChunkedEncodingReadStream(HttpConnection connection, HttpResponseMessage response):base (connection)
        {
            _connection = connection;
            _response = response;
        }
        public override int Read(Span<byte> buffer)
        {
            if (_connection == null || buffer.Length == 0)
            {
                return 0;
            }

            int bytesRead = ReadChunksFromConnectionBuffer(buffer, cancellationRegistration: default(CancellationTokenRegistration));
            if (bytesRead > 0)
            {
                return bytesRead;
            }

            while (true)
            {
                if (_connection == null)
                {
                    return 0;
                }

                if (_state == ParsingState.ExpectChunkData &&
                    buffer.Length >= _connection.ReadBufferSize &&
                    _chunkBytesRemaining >= (ulong)_connection.ReadBufferSize)
                {
                    bytesRead = _connection.Read(buffer.Slice(0, (int)Math.Min((ulong)buffer.Length, _chunkBytesRemaining)));
                    if (bytesRead == 0)
                    {
                        throw new IOException("net_http_invalid_response_premature_eof_bytecount");
                    }
                    _chunkBytesRemaining -= (ulong)bytesRead;
                    if (_chunkBytesRemaining == 0)
                    {
                        _state = ParsingState.ExpectChunkTerminator;
                    }
                    return bytesRead;
                }

                _connection.Fill();

                int bytesCopied = ReadChunksFromConnectionBuffer(buffer, cancellationRegistration: default(CancellationTokenRegistration));
                if (bytesCopied > 0)
                {
                    return bytesCopied;
                }
            }
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            }

            if (_connection == null || buffer.Length == 0)
            {
                return new ValueTask<int>(0);
            }

            int bytesRead = ReadChunksFromConnectionBuffer(buffer.Span, cancellationRegistration: default(CancellationTokenRegistration));
            if (bytesRead > 0)
            {
                return new ValueTask<int>(bytesRead);
            }

            if (_connection == null)
            {
                return new ValueTask<int>(0);
            }

            return ReadAsyncCore(buffer, cancellationToken);
        }

        private async ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            CancellationTokenRegistration ctr = _connection.RegisterCancellation(cancellationToken);
            try
            {
                while (true)
                {
                    if (_connection == null)
                    {
                        return 0;
                    }

                    if (_state == ParsingState.ExpectChunkData &&
                        buffer.Length >= _connection.ReadBufferSize &&
                        _chunkBytesRemaining >= (ulong)_connection.ReadBufferSize)
                    {
                        int bytesRead = await _connection.ReadAsync(buffer.Slice(0, (int)Math.Min((ulong)buffer.Length, _chunkBytesRemaining))).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            throw new IOException("net_http_invalid_response_premature_eof_bytecount");
                        }
                        _chunkBytesRemaining -= (ulong)bytesRead;
                        if (_chunkBytesRemaining == 0)
                        {
                            _state = ParsingState.ExpectChunkTerminator;
                        }
                        return bytesRead;
                    }

                    await _connection.FillAsync().ConfigureAwait(false);

                    int bytesCopied = ReadChunksFromConnectionBuffer(buffer.Span, ctr);
                    if (bytesCopied > 0)
                    {
                        return bytesCopied;
                    }
                }
            }
            finally
            {
                ctr.Dispose();
            }
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            return
                cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) :
                _connection == null ? Task.CompletedTask :
                CopyToAsyncCore(destination, cancellationToken);
        }

        private async Task CopyToAsyncCore(Stream destination, CancellationToken cancellationToken)
        {
            CancellationTokenRegistration ctr = _connection.RegisterCancellation(cancellationToken);
            try
            {
                while (true)
                {
                    while (true)
                    {
                        ReadOnlyMemory<byte> bytesRead = ReadChunkFromConnectionBuffer(int.MaxValue, ctr);
                        if (bytesRead.Length == 0)
                        {
                            break;
                        }
                        await destination.WriteAsync(bytesRead, cancellationToken).ConfigureAwait(false);
                    }

                    if (_connection == null)
                    {
                        return;
                    }

                    await _connection.FillAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exc) when (!(exc is OperationCanceledException) && cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            finally
            {
                ctr.Dispose();
            }
        }

        private int ReadChunksFromConnectionBuffer(Span<byte> buffer, CancellationTokenRegistration cancellationRegistration)
        {
            int totalBytesRead = 0;
            while (buffer.Length > 0)
            {
                ReadOnlyMemory<byte> bytesRead = ReadChunkFromConnectionBuffer(buffer.Length, cancellationRegistration);
                if (bytesRead.Length == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead.Length;
                bytesRead.Span.CopyTo(buffer);
                buffer = buffer.Slice(bytesRead.Length);
            }
            return totalBytesRead;
        }

        private ReadOnlyMemory<byte> ReadChunkFromConnectionBuffer(int maxBytesToRead, CancellationTokenRegistration cancellationRegistration)
        {
            try
            {
                ReadOnlySpan<byte> currentLine;
                switch (_state)
                {
                    case ParsingState.ExpectChunkHeader:
                        _connection._allowedReadLineBytes = MaxChunkBytesAllowed;
                        if (!_connection.TryReadNextLine(out currentLine))
                        {
                            return default(ReadOnlyMemory<byte>);
                        }

                        if (!Utf8Parser.TryParse(currentLine, out ulong chunkSize, out int bytesConsumed, 'X'))
                        {
                            throw new IOException("net_http_invalid_response_chunk_header_invalid");
                        }
                        _chunkBytesRemaining = chunkSize;

                        if (bytesConsumed != currentLine.Length)
                        {
                            ValidateChunkExtension(currentLine.Slice(bytesConsumed));
                        }

                        if (chunkSize > 0)
                        {
                            _state = ParsingState.ExpectChunkData;
                            goto case ParsingState.ExpectChunkData;
                        }
                        else
                        {
                            _state = ParsingState.ConsumeTrailers;
                            goto case ParsingState.ConsumeTrailers;
                        }

                    case ParsingState.ExpectChunkData:

                        ReadOnlyMemory<byte> connectionBuffer = _connection.RemainingBuffer;
                        if (connectionBuffer.Length == 0)
                        {
                            return default(ReadOnlyMemory<byte>);
                        }

                        int bytesToConsume = Math.Min(maxBytesToRead, (int)Math.Min((ulong)connectionBuffer.Length, _chunkBytesRemaining));

                        _connection.ConsumeFromRemainingBuffer(bytesToConsume);
                        _chunkBytesRemaining -= (ulong)bytesToConsume;
                        if (_chunkBytesRemaining == 0)
                        {
                            _state = ParsingState.ExpectChunkTerminator;
                        }

                        return connectionBuffer.Slice(0, bytesToConsume);

                    case ParsingState.ExpectChunkTerminator:

                        _connection._allowedReadLineBytes = MaxChunkBytesAllowed;
                        if (!_connection.TryReadNextLine(out currentLine))
                        {
                            return default(ReadOnlyMemory<byte>);
                        }

                        if (currentLine.Length != 0)
                        {
                            throw new HttpRequestException("net_http_invalid_response_chunk_terminator_invalid");
                        }

                        _state = ParsingState.ExpectChunkHeader;
                        goto case ParsingState.ExpectChunkHeader;

                    case ParsingState.ConsumeTrailers:

                        while (true)
                        {
                            _connection._allowedReadLineBytes = MaxTrailingHeaderLength;
                            if (!_connection.TryReadNextLine(out currentLine))
                            {
                                break;
                            }

                            if (currentLine.IsEmpty)
                            {
                                cancellationRegistration.Dispose();
                                cancellationRegistration.Token.ThrowIfCancellationRequested();

                                 _state = ParsingState.Done;
                                _connection.CompleteResponse();
                                _connection = null;

                                break;
                            }
                            else if (!IsDisposed)
                            {
                                HttpConnection.ParseHeaderLine(currentLine, _response);
                            }
                        }

                        return default(ReadOnlyMemory<byte>);

                    default:
                    case ParsingState.Done:

                        return default(ReadOnlyMemory<byte>);
                }
            }
            catch (Exception)
            {
                _connection.Dispose();
                _connection = null;
                throw;
            }
        }

        private static void ValidateChunkExtension(ReadOnlySpan<byte> lineAfterChunkSize)
        {

            for (int i = 0; i < lineAfterChunkSize.Length; i++)
            {
                byte c = lineAfterChunkSize[i];
                if (c == ';')
                {
                    break;
                }
                else if (c != ' ' && c != '\t') 
                {
                    throw new IOException("net_http_invalid_response_chunk_extension_invalid");
                }
            }
        }

        private enum ParsingState : byte
        {
            ExpectChunkHeader,
            ExpectChunkData,
            ExpectChunkTerminator,
            ConsumeTrailers,
            Done
        }
    }
}
