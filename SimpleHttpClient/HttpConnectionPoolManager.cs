using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal sealed class HttpConnectionPoolManager : IDisposable
    {
        private readonly ConcurrentDictionary<HttpConnectionKey, HttpConnectionPool> _pools;

        internal readonly HttpConnectionSettings settings;
        private readonly IWebProxy _proxy;

        private bool disposed;
        private object SyncObj => _pools;

        public HttpConnectionPoolManager(HttpConnectionSettings settings)
        {
            _pools = new ConcurrentDictionary<HttpConnectionKey, HttpConnectionPool>();
            this.settings = settings;

            if (settings.UseProxy)
            {
                _proxy = HttpClient.DefaultProxy;
            }
        }

        private static HttpConnectionKey GetConnectionKey(HttpRequestMessage request, Uri proxyUri, bool isProxyConnect)
        {
            Uri uri = request.RequestUri;

            if (isProxyConnect)
            {
                return new HttpConnectionKey(HttpConnectionKind.ProxyConnect, uri.IdnHost, uri.Port, null, proxyUri);
            }

            string sslHostName = null;
            if (HttpUtilities.IsSupportedSecureScheme(uri.Scheme))
            {
                string hostHeader = request.Headers.Host;
                if (hostHeader != null)
                {
                    sslHostName = ParseHostNameFromHeader(hostHeader);
                }
                else
                {
                    // No explicit Host header.  Use host from uri.
                    sslHostName = uri.IdnHost;
                }
            }

            if (proxyUri != null)
            {
                if (sslHostName == null)
                {
                    if (HttpUtilities.IsNonSecureWebSocketScheme(uri.Scheme))
                    {
                        // Non-secure websocket connection through proxy to the destination.
                        return new HttpConnectionKey(HttpConnectionKind.ProxyTunnel, uri.IdnHost, uri.Port, null, proxyUri);
                    }
                    else
                    {
                        // Standard HTTP proxy usage for non-secure requests
                        // The destination host and port are ignored here, since these connections
                        // will be shared across any requests that use the proxy.
                        return new HttpConnectionKey(HttpConnectionKind.Proxy, null, 0, null, proxyUri);
                    }
                }
                else
                {
                    // Tunnel SSL connection through proxy to the destination.
                    return new HttpConnectionKey(HttpConnectionKind.SslProxyTunnel, uri.IdnHost, uri.Port, sslHostName, proxyUri);
                }
            }
            else if (sslHostName != null)
            {
                return new HttpConnectionKey(HttpConnectionKind.Https, uri.IdnHost, uri.Port, sslHostName, null);
            }
            else
            {
                return new HttpConnectionKey(HttpConnectionKind.Http, uri.IdnHost, uri.Port, null, null);
            }
        }

        private static string ParseHostNameFromHeader(string hostHeader)
        {
            // See if we need to trim off a port.
            int colonPos = hostHeader.IndexOf(':');
            if (colonPos >= 0)
            {
                // There is colon, which could either be a port separator or a separator in
                // an IPv6 address.  See if this is an IPv6 address; if it's not, use everything
                // before the colon as the host name, and if it is, use everything before the last
                // colon iff the last colon is after the end of the IPv6 address (otherwise it's a
                // part of the address).
                int ipV6AddressEnd = hostHeader.IndexOf(']');
                if (ipV6AddressEnd == -1)
                {
                    return hostHeader.Substring(0, colonPos);
                }
                else
                {
                    colonPos = hostHeader.LastIndexOf(':');
                    if (colonPos > ipV6AddressEnd)
                    {
                        return hostHeader.Substring(0, colonPos);
                    }
                }
            }

            return hostHeader;
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {

            if (_proxy == null)
            {
                return SendAsyncCore(request, null, false, cancellationToken);
            }

            Uri proxyUri = null;
            try
            {
                if (!_proxy.IsBypassed(request.RequestUri))
                {
                    proxyUri = _proxy.GetProxy(request.RequestUri);
                }
            }
            catch (Exception ex)
            {
                // Eat any exception from the IWebProxy and just treat it as no proxy.
                // This matches the behavior of other handlers.
            }
            if (proxyUri != null && proxyUri.Scheme != "http")
            {
                throw new NotSupportedException("net_http_invalid_proxy_scheme");
            }
            return SendAsyncCore(request, proxyUri, false, cancellationToken);
        }

        public Task<HttpResponseMessage> SendProxyConnectAsync(HttpRequestMessage request, Uri proxyUri, CancellationToken cancellationToken)
        {
            return SendAsyncCore(request, proxyUri, true, cancellationToken);
        }

        public Task<HttpResponseMessage> SendAsyncCore(HttpRequestMessage request, Uri proxyUri, bool isProxyConnect, CancellationToken cancellationToken)
        {
            var key = GetConnectionKey(request, proxyUri, isProxyConnect);
            HttpConnectionPool pool;
            while (!_pools.TryGetValue(key, out pool))
            {
                pool = new HttpConnectionPool(key.Kind, key.Host, key.SslHostName, key.Port, proxyUri,this);
                _pools.TryAdd(key, pool);
            }
            return pool.SendAsync(request, cancellationToken);
        }

        private void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                foreach (var pool in _pools)
                {
                    pool.Value.Dispose();
                }
                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }

    internal static class HttpUtilities
    {
        internal static Version DefaultRequestVersion =>
#if uap
            HttpVersion.Version20;
#else
            HttpVersion.Version11;
#endif

        internal static Version DefaultResponseVersion => HttpVersion.Version11;

        internal static bool IsHttpUri(Uri uri)
        {
            return IsSupportedScheme(uri.Scheme);
        }

        internal static bool IsSupportedScheme(string scheme) =>
            IsSupportedNonSecureScheme(scheme) ||
            IsSupportedSecureScheme(scheme);

        internal static bool IsSupportedNonSecureScheme(string scheme) =>
            string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) || IsNonSecureWebSocketScheme(scheme);

        internal static bool IsSupportedSecureScheme(string scheme) =>
            string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) || IsSecureWebSocketScheme(scheme);

        internal static bool IsNonSecureWebSocketScheme(string scheme) =>
            string.Equals(scheme, "ws", StringComparison.OrdinalIgnoreCase);

        internal static bool IsSecureWebSocketScheme(string scheme) =>
            string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase);

        // Always specify TaskScheduler.Default to prevent us from using a user defined TaskScheduler.Current.
        //
        // Since we're not doing any CPU and/or I/O intensive operations, continue on the same thread.
        // This results in better performance since the continuation task doesn't get scheduled by the
        // scheduler and there are no context switches required.
        internal static Task ContinueWithStandard<T>(this Task<T> task, object state, Action<Task<T>, object> continuation)
        {
            return task.ContinueWith(continuation, state, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
