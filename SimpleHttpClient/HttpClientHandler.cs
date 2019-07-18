using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class HttpClientHandler : HttpMessageHandler
    {
        private HttpConnectionPool httpConnectionPool = new HttpConnectionPool();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return httpConnectionPool.SendAsync(request, cancellationToken);
        }
    }
}
