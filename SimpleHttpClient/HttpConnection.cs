using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class HttpConnection : IDisposable
    {
        private const int InitialReadBufferSize = 4096;
        private const int InitialWriteBufferSize = InitialReadBufferSize;
        private static readonly byte[] s_spaceHttp11NewlineAsciiBytes = Encoding.ASCII.GetBytes(" HTTP/1.1\r\n");

        private readonly Socket _socket; 
        private readonly Stream _stream;

        private readonly byte[] _writeBuffer;
        private int _writeOffset;

        private byte[] _readBuffer;
        private int _readOffset;

        private bool disposed;

        public HttpConnection(
            HttpConnectionPool pool,
            Socket socket,
            Stream stream)
        {
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

            await _stream.WriteAsync(new Memory<byte>(_writeBuffer));

            await _stream.ReadAsync(new Memory<byte>(_readBuffer));

            Console.WriteLine("这就是我最后的波纹了 JOJO");
            Console.WriteLine(Encoding.UTF8.GetString(_readBuffer));

            return null;
        }

        private Task WriteStringAsync(string s)
        {
            int offset = _writeOffset;  
            if (s.Length <= _writeBuffer.Length)
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



        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

    }
}
