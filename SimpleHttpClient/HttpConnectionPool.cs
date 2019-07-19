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
    internal class HttpConnectionPool
    {
        private readonly List<CachedConnection> _idleConnections = new List<CachedConnection>();

        public HttpConnectionPool()
        {
         
        }


        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isHttps = request.RequestUri.Scheme.ToLower().Equals("https");
           var (connection, isNewConnection, failureResponse) = await GetConnectionAsync(isHttps?HttpConnectionKind.Https: HttpConnectionKind.Http, request, cancellationToken);

            return await connection.SendAsync(request, cancellationToken); 
        }

        private async ValueTask<(HttpConnection connection, bool isNewConnection, HttpResponseMessage failureResponse)>
            GetConnectionAsync(HttpConnectionKind kind,HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //that's so complex,let me look some time
            if (false)
            {
                var connection = await GetOrReserveHttpConnectionAsync(cancellationToken);
                if (connection != null)
                {
                    return (connection, false, null);
                }
            }

            var (sokect, stream, failureResponse) = await ConnectAsync(request, cancellationToken);
            if (kind == HttpConnectionKind.Https || kind == HttpConnectionKind.SslProxyTunnel)
            {
                var sslOptions = new SslClientAuthenticationOptions();
                sslOptions.TargetHost = request.RequestUri.IdnHost;

                SslStream sslStream = await EstablishSslConnectionAsync(sslOptions, request, stream, cancellationToken).ConfigureAwait(false);
                stream = sslStream;
            }

            return (ConstructHttpConnection(sokect, stream), true, null);
        }

        private async ValueTask<HttpConnection> GetOrReserveHttpConnectionAsync(CancellationToken cancellationToken)
        {
            var conn = _idleConnections.FirstOrDefault()._connection;

            return conn;
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
                            if (cancellationToken.IsCancellationRequested)
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

                saea.RemoteEndPoint = new DnsEndPoint(request.RequestUri.IdnHost, request.RequestUri.Port);

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
