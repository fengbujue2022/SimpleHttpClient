using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace SimpleHttpClient
{
    public class HttpClientFactory
    {
        public static HttpClient Create(params DelegatingHandler[] handlers)
        {
            return Create((Action<HttpClientHandler>)null, handlers);
        }

        public static HttpClient Create(Action<HttpClientHandler> configure, params DelegatingHandler[] handlers)
        {
            var handler = new HttpClientHandler();
            configure?.Invoke(handler);
            return Create(handler, handlers);
        }

        public static HttpClient Create(HttpMessageHandler innerHandler, params DelegatingHandler[] handlers)
        {
            HttpMessageHandler pipeline = CreatePipeline(innerHandler, handlers);
            return new HttpClient(pipeline);
        }

        public static HttpMessageHandler CreatePipeline(HttpMessageHandler innerHandler, IEnumerable<DelegatingHandler> handlers)
        {
            if (innerHandler == null)
            {
                throw new ArgumentNullException($"{nameof(innerHandler)} can be null");
            }

            if (handlers == null)
            {
                return innerHandler;
            }

            HttpMessageHandler pipeline = innerHandler;
            IEnumerable<DelegatingHandler> reversedHandlers = handlers.Reverse();
            foreach (DelegatingHandler handler in reversedHandlers)
            {
                if (handler == null)
                {
                    throw new ArgumentNullException($"{nameof(handler)}innerHandler can be null");
                }

                if (handler.InnerHandler != null)
                {
                    throw new ArgumentNullException($"{nameof(handler)}innerHandler can be null");
                }

                handler.InnerHandler = pipeline;
                pipeline = handler;
            }

            return pipeline;
        }

    }
}
