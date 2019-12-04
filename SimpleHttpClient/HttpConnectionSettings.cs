using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SimpleHttpClient
{
    public class HttpConnectionSettings
    {
        public EndPointProvider EndPointProvider { get; set; } = new EndPointProvider();
        public DecompressionMethods AutomaticDecompression { get; set; } = DecompressionMethods.None;
        public X509CertificateCollection ClientCertificates { get; set; }
        public RemoteCertificateValidationCallback RemoteCertificateValidationCallback { get; set; }
    }
}
