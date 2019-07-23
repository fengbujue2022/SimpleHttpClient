using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal sealed class HttpConnectionPoolManager : IDisposable
    {
        private readonly ConcurrentDictionary<HttpConnectionKey, HttpConnectionPool> _pools;

        private object SyncObj => _pools;

        public HttpConnectionPoolManager()
        {
            _pools = new ConcurrentDictionary<HttpConnectionKey, HttpConnectionPool>();
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = GetConnectionKey(request);
            HttpConnectionPool pool;
            if (!_pools.TryGetValue(key, out pool))
            {
                pool = new HttpConnectionPool(key.Kind, key.Host, key.SslHostName, key.Port);
                _pools.TryAdd(key, pool);
            }
            return pool.SendAsync(request, cancellationToken);
        }

        private static HttpConnectionKey GetConnectionKey(HttpRequestMessage request)
        {
            var uri = request.RequestUri;
            var isHttps = uri.Scheme.ToLower().Equals("https");
            return new HttpConnectionKey(isHttps ? HttpConnectionKind.Https : HttpConnectionKind.Http, uri.IdnHost, uri.Port, uri.IdnHost); ;
        }

        private bool disposed = false;

        private void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
