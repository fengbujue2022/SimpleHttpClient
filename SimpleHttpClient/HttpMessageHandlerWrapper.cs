using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    public abstract class HttpMessageHandlerWrapper : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("其实我是个抽象方法来着，必须重写哦");
        }

        internal Task<HttpResponseMessage> VisitableSendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return this.SendAsync(request, cancellationToken);
        }
    }
}
