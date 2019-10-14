using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    public class HttpClient : HttpMessageInvoker
    {
        private static readonly TimeSpan s_infiniteTimeout = System.Threading.Timeout.InfiniteTimeSpan;
        private const HttpCompletionOption defaultCompletionOption = HttpCompletionOption.ResponseContentRead;

        private CancellationTokenSource _pendingRequestsCts;
        private volatile bool _disposed;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
        private int _maxResponseContentBufferSize;


        public HttpClient()
            : this(new HttpClientHandler())
        {

        }

        public HttpClient(HttpMessageHandler handler) :
            this(handler, true)
        {
        }

        public HttpClient(HttpMessageHandler handler, bool disposeHandler) : base(handler, disposeHandler)
        {
            _pendingRequestsCts = new CancellationTokenSource();
            _maxResponseContentBufferSize = int.MaxValue;
        }


        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            return SendAsync(request, defaultCompletionOption, CancellationToken.None);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption)
        {
            return SendAsync(request, completionOption, CancellationToken.None);
        }

        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return SendAsync(request, defaultCompletionOption, CancellationToken.None);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        {
            CancellationTokenSource cts;

            bool disposeCts;
            var hasTimeout = this.Timeout != s_infiniteTimeout;
            if (hasTimeout || cancellationToken.CanBeCanceled)
            {
                disposeCts = true;
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _pendingRequestsCts.Token);
                if (hasTimeout)
                {
                    cts.CancelAfter(Timeout);
                }
            }
            else
            {
                disposeCts = false;
                cts = _pendingRequestsCts;
            }

            Task<HttpResponseMessage> sendTask;
            try
            {
                sendTask = base.SendAsync(request, cts.Token);
            }
            catch
            {
                HandleFinishSendAsyncCleanup(cts, disposeCts);
                throw;
            }

            return completionOption == HttpCompletionOption.ResponseContentRead && !string.Equals(request.Method.Method, "HEAD", StringComparison.OrdinalIgnoreCase) ?
                FinishSendAsyncBuffered(sendTask, request, cts, disposeCts) :
                FinishSendAsyncUnbuffered(sendTask, request, cts, disposeCts);
        }

        private async Task<HttpResponseMessage> FinishSendAsyncBuffered(
         Task<HttpResponseMessage> sendTask, HttpRequestMessage request, CancellationTokenSource cts, bool disposeCts)
        {
            HttpResponseMessage response = null;
            try
            {
                // Wait for the send request to complete, getting back the response.
                response = await sendTask.ConfigureAwait(false);
                if (response == null)
                {
                    throw new InvalidOperationException("no response");
                }

                // Buffer the response content if we've been asked to and we have a Content to buffer.
                if (response.Content != null)
                {
                    await response.Content.LoadIntoBufferAsync(_maxResponseContentBufferSize);
                }

                return response;
            }
            catch (Exception e)
            {
                response?.Dispose();
                HandleFinishSendAsyncError(e, cts);
                throw;
            }
            finally
            {
                HandleFinishSendAsyncCleanup(cts, disposeCts);
            }
        }

        private async Task<HttpResponseMessage> FinishSendAsyncUnbuffered(
            Task<HttpResponseMessage> sendTask, HttpRequestMessage request, CancellationTokenSource cts, bool disposeCts)
        {
            try
            {
                HttpResponseMessage response = await sendTask.ConfigureAwait(false);
                if (response == null)
                {
                    throw new InvalidOperationException("no response");
                }
                return response;
            }
            catch (Exception e)
            {
                HandleFinishSendAsyncError(e, cts);
                throw;
            }
            finally
            {
                HandleFinishSendAsyncCleanup(cts, disposeCts);
            }
        }

        private void HandleFinishSendAsyncError(Exception e, CancellationTokenSource cts)
        {
            if (cts.IsCancellationRequested && e is HttpRequestException)
            {
                throw new OperationCanceledException(cts.Token);
            }
        }

        private void HandleFinishSendAsyncCleanup(CancellationTokenSource cts, bool disposeCts)
        {
            if (disposeCts)
            {
                cts.Dispose();
            }
        }

        public void CancelPendingRequests()
        {
            CheckDisposed();
            // With every request we link this cancellation token source.
            CancellationTokenSource currentCts = Interlocked.Exchange(ref _pendingRequestsCts,
                new CancellationTokenSource());

            currentCts.Cancel();
            currentCts.Dispose();
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().ToString());
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;

                _pendingRequestsCts.Cancel();
                _pendingRequestsCts.Dispose();
            }

            base.Dispose(disposing);
        }

    }
}
