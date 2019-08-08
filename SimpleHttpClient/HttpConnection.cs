﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Buffers;

namespace SimpleHttpClient
{
    internal class HttpConnection : IDisposable
    {
        private const int InitialReadBufferSize = 4096;
        private const int InitialWriteBufferSize = InitialReadBufferSize;
        private static readonly byte[] s_spaceHttp11NewlineAsciiBytes = Encoding.ASCII.GetBytes(" HTTP/1.1\r\n");
        private static readonly byte[] s_contentLength0NewlineAsciiBytes = Encoding.ASCII.GetBytes("Content-Length: 0\r\n");
        private static readonly byte[] s_http1DotBytes = Encoding.ASCII.GetBytes("HTTP/1.");
        private static readonly ulong s_http10Bytes = BitConverter.ToUInt64(Encoding.ASCII.GetBytes("HTTP/1.0"));
        private static readonly ulong s_http11Bytes = BitConverter.ToUInt64(Encoding.ASCII.GetBytes("HTTP/1.1"));
        private readonly Socket _socket;
        private readonly Stream _stream;
        private readonly HttpConnectionPool _pool;

        private bool _connectionClose;

        private readonly byte[] _writeBuffer;
        private int _writeOffset;

        private byte[] _readBuffer;
        private int _readOffset;
        private int _readLength;

        private bool disposed;

        public HttpConnection(
            HttpConnectionPool pool,
            Socket socket,
            Stream stream)
        {
            _pool = pool;
            _socket = socket;
            _stream = stream;
            _writeBuffer = new byte[InitialWriteBufferSize];
            _readBuffer = new byte[InitialReadBufferSize];
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //write raw request into stream(NetworkStream)
            //method
            await WriteStringAsync(request.Method.Method).ConfigureAwait(false);
            //blank space
            await WriteByteAsync((byte)' ').ConfigureAwait(false);
            //host and querystring
            await WriteStringAsync(request.RequestUri.PathAndQuery).ConfigureAwait(false);
            //protocol
            await WriteBytesAsync(s_spaceHttp11NewlineAsciiBytes).ConfigureAwait(false);
            //header
            await WriteStringAsync($"Host:{request.RequestUri.IdnHost}\r\n").ConfigureAwait(false);
            await WriteHeaderAsync(request.Headers);

            if (request.Content == null)
            {
                await WriteBytesAsync(s_contentLength0NewlineAsciiBytes).ConfigureAwait(false);
            }
            else
            {
                await WriteHeaderAsync(request.Content.Headers).ConfigureAwait(false);
            }
            //CRLF for header end
            await WriteTwoBytesAsync((byte)'\r', (byte)'\n').ConfigureAwait(false);

            await FlushAsync().ConfigureAwait(false);

            var response = new HttpResponseMessage() { RequestMessage = request, Content = new HttpConnectionResponseContent() };
            ParseStatusLine(await ReadNextResponseHeaderLineAsync().ConfigureAwait(false), response);

            //parse herader
            while (true)
            {
                var headerLine = await ReadNextResponseHeaderLineAsync().ConfigureAwait(false);
                if (headerLine.Count == 1 && headerLine[0] == (byte)'\r')
                    break;

                ParseHeaderLine(headerLine, response);
            }
            if (response.Headers.ConnectionClose.GetValueOrDefault())
            {
                _connectionClose = true;
            }

            Stream responseStream = null;
            
            
            //TODO:  wrap the stream
            var remaining = _readLength - _readOffset;
            var memoryStream = new MemoryStream(_readBuffer, _readOffset, remaining);
            ((HttpConnectionResponseContent)response.Content).SetStream(memoryStream);

            CompleteResponse();

            return response;
        }

        public bool PollRead()
        {
            return _socket.Poll(0, SelectMode.SelectRead);
        }

        private void CompleteResponse()
        {
            if (_readLength != _readOffset)
            {
                _readOffset = _readLength = 0;
                _connectionClose = true;
            }
            ReturnConnectionToPool();
        }

        private void ReturnConnectionToPool()
        {
            if (_connectionClose)
            {
                Dispose();
            }
            else
            {
                _pool.ReturnConnection(this);
            }
        }

        private Task WriteStringAsync(string s)
        {
            int offset = _writeOffset;
            if (s.Length <= _writeBuffer.Length - offset)
            {
                byte[] writeBuffer = _writeBuffer;
                foreach (char c in s)
                {
                    if ((c & 0xFF80) != 0)
                    {
                        throw new HttpRequestException(" 小老弟 怎么回事 编码都能编歪？");
                    }
                    writeBuffer[offset++] = (byte)c;
                }
                _writeOffset = offset;
                return Task.CompletedTask;
            }
            return WriteStringAsyncSlow(s);
        }

        private Task WriteAsciiStringAsync(string s)
        {
            int offset = _writeOffset;
            if (s.Length <= _writeBuffer.Length - offset)
            {
                byte[] writeBuffer = _writeBuffer;
                foreach (char c in s)
                {
                    writeBuffer[offset++] = (byte)c;
                }
                _writeOffset = offset;
                return Task.CompletedTask;
            }

            return WriteStringAsyncSlow(s);
        }

        private async Task WriteStringAsyncSlow(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c & 0xFF80) != 0)
                {
                    throw new HttpRequestException("小老弟 怎么回事  编码都能编歪？");
                }
                await WriteByteAsync((byte)c).ConfigureAwait(false);
            }
        }

        private Task WriteByteAsync(byte b)
        {
            if (_writeOffset < _writeBuffer.Length)
            {
                _writeBuffer[_writeOffset++] = b;
                return Task.CompletedTask;
            }
            return WriteByteSlowAsync(b);
        }

        private Task WriteBytesAsync(byte[] bytes)
        {
            if (_writeOffset <= _writeBuffer.Length - bytes.Length)
            {
                Buffer.BlockCopy(bytes, 0, _writeBuffer, _writeOffset, bytes.Length);
                _writeOffset += bytes.Length;
                return Task.CompletedTask;
            }
            return WriteBytesSlowAsync(bytes);
        }

        private Task WriteTwoBytesAsync(byte b1, byte b2)
        {
            if (_writeOffset <= _writeBuffer.Length - 2)
            {
                byte[] buffer = _writeBuffer;
                buffer[_writeOffset++] = b1;
                buffer[_writeOffset++] = b2;
                return Task.CompletedTask;
            }
            return WriteTwoBytesSlowAsync(b1, b2);
        }

        private async Task WriteTwoBytesSlowAsync(byte b1, byte b2)
        {
            await WriteByteAsync(b1).ConfigureAwait(false);
            await WriteByteAsync(b2).ConfigureAwait(false);
        }

        private async Task WriteBytesSlowAsync(byte[] bytes)
        {
            int offset = 0;
            while (true)
            {
                int remaining = bytes.Length - offset;
                int toCopy = Math.Min(remaining, _writeBuffer.Length - _writeOffset);
                Buffer.BlockCopy(bytes, offset, _writeBuffer, _writeOffset, toCopy);
                _writeOffset += toCopy;
                offset += toCopy;

                if (offset == bytes.Length)
                {
                    break;
                }
                else if (_writeOffset == _writeBuffer.Length)
                {
                    await WriteToStreamAsync(_writeBuffer).ConfigureAwait(false);
                    _writeOffset = 0;
                }
            }
        }

        private async Task WriteByteSlowAsync(byte b)
        {
            await WriteToStreamAsync(_writeBuffer).ConfigureAwait(false);

            _writeBuffer[0] = b;
            _writeOffset = 1;
        }

        private ValueTask WriteToStreamAsync(ReadOnlyMemory<byte> source)
        {
            return _stream.WriteAsync(source);
        }

        private async Task WriteHeaderAsync(HttpHeaders headers)
        {
            foreach (var header in headers)
            {
                await WriteAsciiStringAsync(header.Key).ConfigureAwait(false);
                await WriteTwoBytesAsync((byte)':', (byte)' ').ConfigureAwait(false);
                foreach (var value in header.Value)
                {
                    await WriteStringAsync(value).ConfigureAwait(false);
                    if (header.Value.Count() > 1)
                    {
                        await WriteTwoBytesAsync((byte)';', (byte)' ').ConfigureAwait(false);
                    }
                }
                await WriteStringAsync("\r\n").ConfigureAwait(false);
            }
        }

        private ValueTask FlushAsync()
        {
            if (_writeOffset > 0)
            {
                ValueTask t = WriteToStreamAsync(new ReadOnlyMemory<byte>(_writeBuffer, 0, _writeOffset));
                _writeOffset = 0;
                return t;
            }
            return default(ValueTask);
        }

        private async ValueTask<ArraySegment<byte>> ReadNextResponseHeaderLineAsync()
        {
            while (true)
            {
                int lfIndex = Array.IndexOf(_readBuffer, (byte)'\n', _readOffset);
                if (lfIndex >= 0)
                {
                    if (_readBuffer[lfIndex - 1] == (byte)'\r')
                    {
                        var segments = new ArraySegment<byte>(_readBuffer, _readOffset, lfIndex - _readOffset);
                        _readOffset = lfIndex + 1;
                        return segments;
                    }
                }
                else
                {
                    await FillAsync().ConfigureAwait(false);
                }
            }
        }

        //TODO: need to resize buffer when header line size greater than 4k
        private async Task FillAsync()
        {
            var remaining = _readLength - _readOffset;

            if (remaining == 0)
            {
                _readOffset = _readLength = 0;
            }
            else if (remaining > 0)//merge unread buffer to new buffer
            {
                var newReadBuffer = new byte[_readBuffer.Length];
                Buffer.BlockCopy(_readBuffer, _readOffset, newReadBuffer, 0, remaining);
                _readBuffer = newReadBuffer;
                _readOffset = 0;
            }
            var bytesRead = await _stream.ReadAsync(new Memory<byte>(_readBuffer, 0, _readBuffer.Length - _readLength));
            _readLength += bytesRead;
        }


        private static bool EqualsOrdinal(string left, Span<byte> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsDigit(byte c) => (uint)(c - '0') <= '9' - '0';

        private static void ParseStatusLine(ArraySegment<byte> line, HttpResponseMessage response) =>
           ParseStatusLine((Span<byte>)line, response);

        private static void ParseStatusLine(Span<byte> line, HttpResponseMessage response)
        {
            var MinStatusLineLength = 13;

            ulong first8Bytes = BitConverter.ToUInt64(line);

            if (first8Bytes == s_http11Bytes)
            {
                response.Version = HttpVersion.Version11;
            }
            else if (first8Bytes == s_http10Bytes)
            {
                response.Version = HttpVersion.Version10;
            }
            else
            {
                byte minorVersion = line[7];
                if (IsDigit(minorVersion) &&
                    line.Slice(0, 7).SequenceEqual(s_http1DotBytes))
                {
                    response.Version = new Version(1, minorVersion - '0');
                }
                else
                {
                    throw new HttpRequestException("无效的响应状态行");
                }
            }
            byte status1 = line[9], status2 = line[10], status3 = line[11];
            if (!IsDigit(status1) || !IsDigit(status2) || !IsDigit(status3))
            {
                throw new HttpRequestException("无效的状态码");
            }
            response.StatusCode = ((HttpStatusCode)(100 * (status1 - '0') + 10 * (status2 - '0') + (status3 - '0')));

            if (line.Length == MinStatusLineLength)
            {
                response.ReasonPhrase = string.Empty;
            }
            else if (line[MinStatusLineLength] == ' ')
            {
                Span<byte> reasonBytes = line.Slice(MinStatusLineLength + 1);
                string knownReasonPhrase = HttpStatusDescription.Get(response.StatusCode);
                response.ReasonPhrase = EqualsOrdinal(knownReasonPhrase, reasonBytes) ?
                    knownReasonPhrase :
                    reasonBytes.ToString();
            }
        }

        private static void ParseHeaderLine(Span<byte> line, HttpResponseMessage response)
        {
            var pos = 0;
            while (line[pos] != (byte)':')
            {
                pos++;
                if (pos == line.Length)
                {
                    throw new HttpRequestException(Encoding.ASCII.GetString(line));
                }
            }

            if (pos == 0)
            {
                throw new HttpRequestException("无效的空header");
            }

            var headerName = Encoding.UTF8.GetString(line.Slice(0, pos));

            while (line[pos] != (byte)' ')
            {
                pos++;
                if (pos == line.Length)
                {
                    throw new HttpRequestException(Encoding.ASCII.GetString(line));
                }
            }

            var headerValue = Encoding.UTF8.GetString(line.Slice(++pos));

            response.Headers.TryAddWithoutValidation(headerName, headerValue);
        }


        public void Dispose() => Dispose(true);

        private void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                disposed = true;
                _stream.Dispose();
                _pool.DecrementConnectionCount();
                GC.SuppressFinalize(this);
            }
        }

    }
}
