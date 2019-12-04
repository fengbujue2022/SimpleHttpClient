using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class DecompressionHandler : HttpMessageHandlerWrapper
    {
        private readonly DecompressionMethods _decompressionMethods;
        private readonly HttpMessageHandlerWrapper _innerHandler;
        private const string s_gzip = "gzip";
        private const string s_deflate = "deflate";
        private static readonly StringWithQualityHeaderValue s_gzipHeaderValue = new StringWithQualityHeaderValue(s_gzip);
        private static readonly StringWithQualityHeaderValue s_deflateHeaderValue = new StringWithQualityHeaderValue(s_deflate);


        public DecompressionHandler(DecompressionMethods decompressionMethods, HttpMessageHandlerWrapper innerHandler)
        {
            _decompressionMethods = decompressionMethods;
            _innerHandler = innerHandler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_decompressionMethods == DecompressionMethods.GZip && !request.Headers.AcceptEncoding.Contains(s_gzipHeaderValue))
            {
                request.Headers.AcceptEncoding.Add(s_gzipHeaderValue);
            }
            if (_decompressionMethods == DecompressionMethods.Deflate && !request.Headers.AcceptEncoding.Contains(s_deflateHeaderValue))
            {
                request.Headers.AcceptEncoding.Add(s_deflateHeaderValue);
            }

            var response =await  _innerHandler.VisitableSendAsync(request, cancellationToken);

            ICollection<string> contentEncodings = response.Content.Headers.ContentEncoding;
            if (contentEncodings.Count > 0)
            {
                string last = null;
                foreach (string encoding in contentEncodings)
                {
                    last = encoding;
                }

                if (_decompressionMethods == DecompressionMethods.GZip && last == s_gzip)
                {
                    response.Content = new GZipDecompressedContent(response.Content);
                }
                else if (_decompressionMethods == DecompressionMethods.Deflate && last == s_deflate)
                {
                    response.Content = new DeflateDecompressedContent(response.Content);
                }
            }

            return response;
        }
    }
}
