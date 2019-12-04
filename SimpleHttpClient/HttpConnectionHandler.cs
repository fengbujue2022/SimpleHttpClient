using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class HttpConnectionHandler : HttpMessageHandlerWrapper
    {
        private readonly HttpConnectionPoolManager _httpConnectionPoolManager;

        public HttpConnectionHandler(HttpConnectionPoolManager httpConnectionPoolManager)
        {
            _httpConnectionPoolManager = httpConnectionPoolManager;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await _httpConnectionPoolManager.SendAsync(request, cancellationToken);
            return response;
        }
    }
}
