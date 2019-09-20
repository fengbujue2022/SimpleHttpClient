using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.IO;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

namespace SimpleHttpClient
{
    internal class HttpConnectionPool : IDisposable
    {
        private readonly List<CachedConnection> _idleConnections;
        private Queue<TaskCompletionSourceWithCancellation<HttpConnection>> _waiters;

        private readonly HttpConnectionKind _kind;
        private readonly string _sslHost;
        private readonly string _host;
        private readonly int _port;
        private readonly HttpClientHandler _handler;

        private bool _disposed;
        private int _associatedConnectionCount;
        private int _maxConnections = 100;
        internal TimeSpan _maxResponseDrainTime = TimeSpan.FromMinutes(10);

        private object SyncObj => _idleConnections;

        public HttpConnectionPool(HttpConnectionKind kind, string host, string sslHost, int port, HttpClientHandler handler)
        {
            _kind = kind;
            _host = host;
            _sslHost = sslHost;
            _port = port;
            _handler = handler;
            _idleConnections = new List<CachedConnection>();
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            while (true)
            {
                var (connection, isNewConnection, failureResponse) = await GetConnectionAsync(_kind, request, cancellationToken);
                var id = connection.Id;
                try
                {
                    return await connection.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException e) when (!isNewConnection)
                {
                    Console.WriteLine($"retry {id}");
                }
            }
        }

        public void ReturnConnection(HttpConnection connection)
        {
            lock (SyncObj)
            {
                bool receivedUnexpectedData = false;
                if (HasWaiter())
                {
                    receivedUnexpectedData = connection.EnsureReadAheadAndPollRead();
                    if (TransferConnection(connection))//set connection result for waiting task
                    {
                        return;
                    }
                }

                var list = _idleConnections;
                if (!receivedUnexpectedData&&!_disposed)
                    list.Add(new CachedConnection(connection));
            }
        }

        public bool TransferConnection(HttpConnection connection)
        {
            TaskCompletionSource<HttpConnection> waiter = DequeueWaiter();
            if (waiter.TrySetResult(connection))
            {
                return true;
            }
            return false;
        }

        private async ValueTask<(HttpConnection connection, bool isNewConnection, HttpResponseMessage failureResponse)>
            GetConnectionAsync(HttpConnectionKind kind, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            /*
            var connection = await GetOrReserveHttpConnectionAsync(cancellationToken);
            if (connection != null)
            {
                return (connection, false, null);
            }
            */
            var (sokect, stream, failureResponse) = await ConnectAsync(kind, request, cancellationToken);

           var connection = ConstructHttpConnection(sokect, stream);

            return (connection, true, null);
        }

        private ValueTask<HttpConnection> GetOrReserveHttpConnectionAsync(CancellationToken cancellationToken)
        {
            CachedConnection cachedConnection = null;
            TaskCompletionSourceWithCancellation<HttpConnection> waiter = default(TaskCompletionSourceWithCancellation<HttpConnection>);
            lock (SyncObj)
            {
                var list = _idleConnections;
                if (list.Count > 0)
                {
                    cachedConnection = list[list.Count - 1];
                    list.RemoveAt(list.Count - 1);
                }
                else
                {
                    if (_associatedConnectionCount < _maxConnections)
                    {
                        IncrementConnectionCountNoLock();
                        return new ValueTask<HttpConnection>((HttpConnection)null);
                    }
                    else
                    {
                        waiter = EnqueueWaiter(cancellationToken);
                    }
                }
            }

            if (cachedConnection != null)
            {
                return new ValueTask<HttpConnection>(cachedConnection._connection);
            }
            else
            {
                //wait for connection return to pool
                return new ValueTask<HttpConnection>(waiter.WaitWithCancellationAsync(cancellationToken));
            }
        }

        private async ValueTask<(Socket, Stream, HttpResponseMessage)> ConnectAsync(HttpConnectionKind kind, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (sokect, stream, failureResponse) = await ConnectAsync(request, cancellationToken);
            if (kind == HttpConnectionKind.Https || kind == HttpConnectionKind.SslProxyTunnel)
            {
                var sslOptions = new SslClientAuthenticationOptions();
                sslOptions.TargetHost = _handler.EndPointProvider.GetHost(_sslHost);
                sslOptions.EnabledSslProtocols = SslProtocols.Tls;
                sslOptions.RemoteCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                 {
                     //TODO
                     return true;
                 };
                SslStream sslStream = await EstablishSslConnectionAsync(sslOptions, request, stream, cancellationToken).ConfigureAwait(false);
                stream = sslStream;
            }

            return (sokect, stream, failureResponse);
        }

        private async ValueTask<(Socket, Stream, HttpResponseMessage)> ConnectAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //TODO: this object need pooling
            var saea = new SocketAsyncEventArgs();

            var taskBuilder = new AsyncTaskMethodBuilder();
            _ = taskBuilder.Task;

            saea.Completed += (s, args) =>
            {
                switch (args.SocketError)
                {
                    case SocketError.Success:
                        taskBuilder.SetResult();
                        break;
                    case SocketError.OperationAborted:
                    case SocketError.ConnectionAborted:
                        if (cancellationToken.CanBeCanceled)
                        {
                            taskBuilder.SetException(new OperationCanceledException("JOJO 我不做人啦！"));
                            break;
                        }
                        goto default;
                    default:
                        taskBuilder.SetException(new SocketException((int)args.SocketError));
                        break;
                }
            };

            saea.RemoteEndPoint = _handler.EndPointProvider.GetEndPoint(_host, _port);

            if (Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, saea))
            {
                using (cancellationToken.Register((s) => Socket.CancelConnectAsync((SocketAsyncEventArgs)s), saea))
                {
                    //waiting for Completed event emit
                    await taskBuilder.Task.ConfigureAwait(false);
                }
            }

            var socket = saea.ConnectSocket;
            var stream = new NetworkStream(socket);

            return (socket, stream, null);
        }


        private static ValueTask<SslStream> EstablishSslConnectionAsync(SslClientAuthenticationOptions sslOptions, HttpRequestMessage request, Stream stream, CancellationToken cancellationToken)
        {
            RemoteCertificateValidationCallback callback = sslOptions.RemoteCertificateValidationCallback;
            if (callback != null && callback.Target is CertificateCallbackMapper mapper)
            {
                sslOptions = sslOptions.ShallowClone();
                Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> localFromHttpClientHandler = mapper.FromHttpClientHandler;
                HttpRequestMessage localRequest = request;
                sslOptions.RemoteCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                    localFromHttpClientHandler(localRequest, certificate as X509Certificate2, chain, sslPolicyErrors);
            }

            return EstablishSslConnectionAsyncCore(stream, sslOptions, cancellationToken);
        }

        private static async ValueTask<SslStream> EstablishSslConnectionAsyncCore(Stream stream, SslClientAuthenticationOptions sslOptions, CancellationToken cancellationToken)
        {
            SslStream sslStream = new SslStream(stream);

            try
            {
                await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                sslStream.Dispose();

                if (e is OperationCanceledException)
                {
                    throw;
                }

                if (!(e is OperationCanceledException) && cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw new HttpRequestException("ssl 连接失败", e);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                sslStream.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            return sslStream;
        }

        private HttpConnection ConstructHttpConnection(Socket socket, Stream stream)
        {
            return new HttpConnection(this, socket, stream);
        }

        public void IncrementConnectionCount()
        {
            lock (SyncObj)
            {
                IncrementConnectionCountNoLock();
            }
        }

        public void IncrementConnectionCountNoLock()
        {
            _associatedConnectionCount++;
        }

        public void DecrementConnectionCount()
        {
            lock (SyncObj)
            {
                _associatedConnectionCount--;
            }
        }

        private bool HasWaiter()
        {
            return (_waiters != null && _waiters.Count > 0);
        }

        private TaskCompletionSourceWithCancellation<HttpConnection> EnqueueWaiter(CancellationToken cancellationToken)
        {
            if (_waiters == null)
            {
                _waiters = new Queue<TaskCompletionSourceWithCancellation<HttpConnection>>();
            }

            var waiter = new TaskCompletionSourceWithCancellation<HttpConnection>();
            _waiters.Enqueue(waiter);
            return waiter;
        }

        private TaskCompletionSourceWithCancellation<HttpConnection> DequeueWaiter()
        {
            return _waiters.Dequeue();
        }

        public void Dispose()
        {
            List<CachedConnection> list = _idleConnections;
            lock (SyncObj)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    list.ForEach(c => c._connection.Dispose());
                    list.Clear();
                }
            }
        }
    }

    internal sealed class CertificateCallbackMapper
    {
        public readonly Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> FromHttpClientHandler;

        public readonly RemoteCertificateValidationCallback ForSocketsHttpHandler;

        public CertificateCallbackMapper(Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> fromHttpClientHandler)
        {
            FromHttpClientHandler = fromHttpClientHandler;
            ForSocketsHttpHandler = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                FromHttpClientHandler(sender as HttpRequestMessage, certificate as X509Certificate2, chain, sslPolicyErrors);
        }
    }

    internal static class SslClientAuthenticationOptionsExtensions
    {
        public static SslClientAuthenticationOptions ShallowClone(this SslClientAuthenticationOptions options) =>
            new SslClientAuthenticationOptions()
            {
                AllowRenegotiation = options.AllowRenegotiation,
                ApplicationProtocols = options.ApplicationProtocols,
                CertificateRevocationCheckMode = options.CertificateRevocationCheckMode,
                ClientCertificates = options.ClientCertificates,
                EnabledSslProtocols = options.EnabledSslProtocols,
                EncryptionPolicy = options.EncryptionPolicy,
                LocalCertificateSelectionCallback = options.LocalCertificateSelectionCallback,
                RemoteCertificateValidationCallback = options.RemoteCertificateValidationCallback,
                TargetHost = options.TargetHost
            };
    }
}
