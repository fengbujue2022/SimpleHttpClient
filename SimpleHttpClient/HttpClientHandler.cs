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
        private HttpConnectionPoolManager httpConnectionPoolManager = new HttpConnectionPoolManager();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return httpConnectionPoolManager.SendAsync(request, cancellationToken);
        }
    }
}
