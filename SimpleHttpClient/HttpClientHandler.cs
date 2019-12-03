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
    public class HttpClientHandler : HttpMessageHandler
    {
        private HttpConnectionPoolManager httpConnectionPoolManager;
        private const string s_gzip = "gzip";
        private const string s_deflate = "deflate";
        private static readonly StringWithQualityHeaderValue s_gzipHeaderValue = new StringWithQualityHeaderValue(s_gzip);
        private static readonly StringWithQualityHeaderValue s_deflateHeaderValue = new StringWithQualityHeaderValue(s_deflate);

        public EndPointProvider EndPointProvider { get; set; } = new EndPointProvider();
        public DecompressionMethods AutomaticDecompression { get; set; } = DecompressionMethods.None;
        public X509CertificateCollection ClientCertificates { get; set; }
        public RemoteCertificateValidationCallback RemoteCertificateValidationCallback { get; set; }

        public HttpClientHandler()
        {
            httpConnectionPoolManager = new HttpConnectionPoolManager();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (AutomaticDecompression == DecompressionMethods.GZip && !request.Headers.AcceptEncoding.Contains(s_gzipHeaderValue))
            {
                request.Headers.AcceptEncoding.Add(s_gzipHeaderValue);
            }
            if (AutomaticDecompression == DecompressionMethods.Deflate && !request.Headers.AcceptEncoding.Contains(s_deflateHeaderValue))
            {
                request.Headers.AcceptEncoding.Add(s_deflateHeaderValue);
            }

            var response = await httpConnectionPoolManager.SendAsync(this, request, cancellationToken);

            ICollection<string> contentEncodings = response.Content.Headers.ContentEncoding;
            if (contentEncodings.Count > 0)
            {
                string last = null;
                foreach (string encoding in contentEncodings)
                {
                    last = encoding;
                }

                if (AutomaticDecompression == DecompressionMethods.GZip && last == s_gzip)
                {
                    response.Content = new GZipDecompressedContent(response.Content);
                }
                else if (AutomaticDecompression == DecompressionMethods.Deflate && last == s_deflate)
                {
                    response.Content = new DeflateDecompressedContent(response.Content);
                }
            }

            return response;
        }
    }
}
