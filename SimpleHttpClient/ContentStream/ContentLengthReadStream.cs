using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class ContentLengthReadStream : HttpContentReadStream
    {
        private ulong _contentBytesRemaining;

        public ContentLengthReadStream(HttpConnection connection, ulong contentLength) : base(connection)
        {
            _contentBytesRemaining = contentLength;
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(Span<byte> buffer)
        {
            if (_connection == null || buffer.Length == 0)
            {
                return 0;
            }

            if ((ulong)buffer.Length > _contentBytesRemaining)
            {
                buffer = buffer.Slice(0, (int)_contentBytesRemaining);
            }

            int bytesRead = _connection.Read(buffer);
            if (bytesRead <= 0)
            {
                throw new IOException("莫得读");
            }

            _contentBytesRemaining -= (ulong)bytesRead;

            if (_contentBytesRemaining == 0)
            {
                _connection.CompleteResponse();
                _connection = null;
            }

            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_connection == null || buffer.Length == 0)
            {
                return 0;
            }

            if ((ulong)buffer.Length > _contentBytesRemaining)
            {
                buffer = buffer.Slice(0, (int)_contentBytesRemaining);
            }

            ValueTask<int> readTask = _connection.ReadAsync(buffer);
            int bytesRead;
            if (readTask.IsCompletedSuccessfully)
            {
                bytesRead = readTask.Result;
            }
            else
            {
                CancellationTokenRegistration ctr = _connection.RegisterCancellation(cancellationToken);
                try
                {
                    bytesRead = await readTask.ConfigureAwait(false);
                }
                catch (Exception exc) when (!(exc is OperationCanceledException) && cancellationToken.IsCancellationRequested)
                {
                    throw exc;
                }
                finally
                {
                    ctr.Dispose();
                }
            }

            if (bytesRead <= 0)
            {
                throw new IOException("莫得读");
            }
            _contentBytesRemaining -= (ulong)bytesRead;

            if (_contentBytesRemaining == 0)
            {
                _connection.CompleteResponse();
                _connection = null;
            }

            return bytesRead;
        }


        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (_connection == null)
            {
                return Task.CompletedTask;
            }

            Task copyTask = _connection.CopyToContentLengthAsync(destination, _contentBytesRemaining, bufferSize, cancellationToken);
            if (copyTask.IsCompletedSuccessfully)
            {
                Finish();
                return Task.CompletedTask;
            }

            return CompleteCopyToAsync(copyTask, cancellationToken);
        }

        private async Task CompleteCopyToAsync(Task copyTask, CancellationToken cancellationToken)
        {
            CancellationTokenRegistration ctr = _connection.RegisterCancellation(cancellationToken);
            try
            {
                await copyTask.ConfigureAwait(false);
            }
            catch (Exception exc) when (!(exc is OperationCanceledException) && cancellationToken.IsCancellationRequested)
            {
                throw exc;
            }
            finally
            {
                ctr.Dispose();
            }

            Finish();
        }

        private void Finish()
        {
            _contentBytesRemaining = 0;
            _connection.CompleteResponse();
            _connection = null;
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
    }
}
