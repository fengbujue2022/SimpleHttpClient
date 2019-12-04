using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    public class HttpClientHandler : HttpMessageHandlerWrapper
    {
        private HttpMessageHandlerWrapper _handler;

        public HttpConnectionSettings Settings = new HttpConnectionSettings();

        private HttpMessageHandlerWrapper SetupHandlerChain()
        {
            var poolManager = new HttpConnectionPoolManager(Settings);
            HttpMessageHandlerWrapper handler = new HttpConnectionHandler(poolManager);
            if (Settings.AutomaticDecompression != DecompressionMethods.None)
            {
                handler = new DecompressionHandler(Settings.AutomaticDecompression, handler);
            }
            return handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _handler = _handler ?? SetupHandlerChain();
            return _handler.VisitableSendAsync(request, cancellationToken);
        }
    }
}
