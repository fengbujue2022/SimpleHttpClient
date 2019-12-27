using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal abstract class HttpBaseStream : Stream
    {
        public sealed override bool CanSeek => false;

        public sealed override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public sealed override void SetLength(long value) => throw new NotSupportedException();

        public sealed override long Length => throw new NotSupportedException();

        public sealed override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
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

        /// <summary>
        /// Validate the arguments to CopyTo, as would Stream.CopyTo, but with knowledge that
        /// the source stream is always readable and so only checking the destination.
        /// </summary>
        protected static void ValidateCopyToArgs(Stream source, Stream destination, int bufferSize)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize), bufferSize, "ArgumentOutOfRange_NeedPosNum");
            }

            if (!destination.CanWrite)
            {
                throw destination.CanRead ?
                    new NotSupportedException("NotSupported_UnwritableStream") :
                    (Exception)new ObjectDisposedException(nameof(destination), "ObjectDisposed_StreamClosed");
            }
        }

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

        public override void Write(byte[] buffer, int offset, int count)
        {
            // This does sync-over-async, but it also should only end up being used in strange
            // situations.  Either a derived stream overrides this anyway, so the implementation won't be used,
            // or it's being called as part of HttpContent.SerializeToStreamAsync, which means custom
            // content is explicitly choosing to make a synchronous call as part of an asynchronous method.
            ValidateBufferArgs(buffer, offset, count);
            WriteAsync(new Memory<byte>(buffer, offset, count), CancellationToken.None).GetAwaiter().GetResult();
        }

        public sealed override void WriteByte(byte value) =>
            Write(MemoryMarshal.CreateReadOnlySpan(ref value, 1));

        public sealed override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArgs(buffer, offset, count);
            return WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override void Flush() => FlushAsync(default(CancellationToken)).GetAwaiter().GetResult();

        public override Task FlushAsync(CancellationToken cancellationToken) => NopAsync(cancellationToken);

        protected static Task NopAsync(CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) :
            Task.CompletedTask;


        public abstract override int Read(Span<byte> buffer);
        public abstract override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
        public abstract override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
    }

    internal abstract class HttpContentStream : HttpBaseStream
    {
        protected HttpConnection _connection;

        public HttpContentStream(HttpConnection connection)
        {
            _connection = connection;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_connection != null)
                {
                    _connection.Dispose();
                    _connection = null;
                }
            }

            base.Dispose(disposing);
        }

        protected HttpConnection GetConnectionOrThrow()
        {
            return _connection ??
                // This should only ever happen if the user-code that was handed this instance disposed of
                // it, which is misuse, or held onto it and tried to use it later after we've disposed of it,
                // which is also misuse.
                ThrowObjectDisposedException();
        }

        private HttpConnection ThrowObjectDisposedException() => throw new ObjectDisposedException(GetType().Name);
    }

    internal sealed class RawConnectionStream : HttpContentStream
    {
        public RawConnectionStream(HttpConnection connection) : base(connection)
        {
        }

        public sealed override bool CanRead => true;
        public sealed override bool CanWrite => true;

        public override int Read(Span<byte> buffer)
        {
            HttpConnection connection = _connection;
            if (connection == null || buffer.Length == 0)
            {
                // Response body fully consumed or the caller didn't ask for any data
                return 0;
            }

            int bytesRead = connection.ReadBuffered(buffer);
            if (bytesRead == 0)
            {
                // We cannot reuse this connection, so close it.
                _connection = null;
                connection.Dispose();
            }

            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpConnection connection = _connection;
            if (connection == null || buffer.Length == 0)
            {
                // Response body fully consumed or the caller didn't ask for any data
                return 0;
            }

            ValueTask<int> readTask = connection.ReadBufferedAsync(buffer);
            int bytesRead;
            if (readTask.IsCompletedSuccessfully)
            {
                bytesRead = readTask.Result;
            }
            else
            {
                CancellationTokenRegistration ctr = connection.RegisterCancellation(cancellationToken);
                try
                {
                    bytesRead = await readTask.ConfigureAwait(false);
                }
                finally
                {
                    ctr.Dispose();
                }
            }

            if (bytesRead == 0)
            {
                // A cancellation request may have caused the EOF.
                cancellationToken.ThrowIfCancellationRequested();

                // We cannot reuse this connection, so close it.
                _connection = null;
                connection.Dispose();
            }

            return bytesRead;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            ValidateCopyToArgs(this, destination, bufferSize);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            HttpConnection connection = _connection;
            if (connection == null)
            {
                // null if response body fully consumed
                return Task.CompletedTask;
            }

            Task copyTask = connection.CopyToUntilEofAsync(destination, bufferSize, cancellationToken);
            if (copyTask.IsCompletedSuccessfully)
            {
                Finish(connection);
                return Task.CompletedTask;
            }

            return CompleteCopyToAsync(copyTask, connection, cancellationToken);
        }

        private async Task CompleteCopyToAsync(Task copyTask, HttpConnection connection, CancellationToken cancellationToken)
        {
            CancellationTokenRegistration ctr = connection.RegisterCancellation(cancellationToken);
            try
            {
                await copyTask.ConfigureAwait(false);
            }
            finally
            {
                ctr.Dispose();
            }

            // If cancellation is requested and tears down the connection, it could cause the copy
            // to end early but think it ended successfully. So we prioritize cancellation in this
            // race condition, and if we find after the copy has completed that cancellation has
            // been requested, we assume the copy completed due to cancellation and throw.
            cancellationToken.ThrowIfCancellationRequested();

            Finish(connection);
        }

        private void Finish(HttpConnection connection)
        {
            // We cannot reuse this connection, so close it.
            connection.Dispose();
            _connection = null;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArgs(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            HttpConnection connection = _connection;
            if (connection == null)
            {
                throw new IOException("ObjectDisposed_StreamClosed");
            }

            if (buffer.Length != 0)
            {
                connection.WriteWithoutBuffering(buffer);
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask(Task.FromCanceled(cancellationToken));
            }

            HttpConnection connection = _connection;
            if (connection == null)
            {
                return new ValueTask(Task.FromException(new IOException("ObjectDisposed_StreamClosed")));
            }

            if (buffer.Length == 0)
            {
                return default(ValueTask);
            }

            ValueTask writeTask = connection.WriteWithoutBufferingAsync(buffer);
            return writeTask.IsCompleted ?
                writeTask :
                new ValueTask(WaitWithConnectionCancellationAsync(writeTask, connection, cancellationToken));
        }

        public override void Flush() => _connection?.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            HttpConnection connection = _connection;
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            ValueTask flushTask = connection.FlushAsync();
            return flushTask.IsCompleted ?
                flushTask.AsTask() :
                WaitWithConnectionCancellationAsync(flushTask, connection, cancellationToken);
        }

        private static async Task WaitWithConnectionCancellationAsync(ValueTask task, HttpConnection connection, CancellationToken cancellationToken)
        {
            CancellationTokenRegistration ctr = connection.RegisterCancellation(cancellationToken);
            try
            {
                await task.ConfigureAwait(false);
            }
            finally
            {
                ctr.Dispose();
            }
        }
    }
}
