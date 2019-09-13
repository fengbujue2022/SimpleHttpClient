using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    public class HttpClientHandler : HttpMessageHandler
    {
        private HttpConnectionPoolManager httpConnectionPoolManager;

        public EndPointProvider EndPointProvider { get; set; } = new EndPointProvider();

        public HttpClientHandler()
        {
            httpConnectionPoolManager = new HttpConnectionPoolManager();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return httpConnectionPoolManager.SendAsync(this, request, cancellationToken);
        }
    }
}
