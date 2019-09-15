using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal abstract class HttpContentReadStream : Stream
    {
        private int _disposed; 

        protected HttpConnection _connection;

        public HttpContentReadStream(HttpConnection connection)
        {
            _connection = connection;
        }

        protected bool IsDisposed => _disposed == 1;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public sealed override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public sealed override void SetLength(long value) => throw new NotSupportedException();

        public sealed override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException("net_http_content_readonly_stream");
        public override void Write(byte[] buffer, int offset, int count)=> throw new NotImplementedException();

        public override void Flush() => FlushAsync(default(CancellationToken)).GetAwaiter().GetResult();
        public override Task FlushAsync(CancellationToken cancellationToken) => NopAsync(cancellationToken);

        protected static Task NopAsync(CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) :
            Task.CompletedTask;

        public sealed override int ReadByte()
        {
            byte b = 0;
            return Read(MemoryMarshal.CreateSpan(ref b, 1)) == 1 ? b : -1;
        }

        public sealed override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArgs(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public sealed override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArgs(buffer, offset, count);
            return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }


        protected static void ValidateBufferArgs(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if ((uint)offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if ((uint)count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }


        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (disposing)
            {
                return;
            }

            base.Dispose(disposing);
        }

        public abstract override int Read(Span<byte> buffer);
        public abstract override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    }
}
