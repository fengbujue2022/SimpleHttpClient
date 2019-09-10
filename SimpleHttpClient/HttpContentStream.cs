using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class HttpConnectionStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        private readonly HttpConnection _httpConnection;

        public HttpConnectionStream(HttpConnection httpConnection) {
            _httpConnection = httpConnection;
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _httpConnection.Read(buffer.AsSpan(offset, count)); 
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,CancellationToken cancellationToken)
        {
            return _httpConnection.ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask();
        }


        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
