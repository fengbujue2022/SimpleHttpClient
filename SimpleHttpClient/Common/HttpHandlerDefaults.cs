using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace SimpleHttpClient.Common
{
    internal static class HttpHandlerDefaults
    {
        public const int DefaultMaxAutomaticRedirections = 50;
        public const int DefaultMaxConnectionsPerServer = int.MaxValue;
        public const int DefaultMaxResponseDrainSize = 1024 * 1024;
        public static readonly TimeSpan DefaultResponseDrainTimeout = TimeSpan.FromSeconds(2);
        public const int DefaultMaxResponseHeadersLength = 64; // Units in K (1024) bytes.
        public const DecompressionMethods DefaultAutomaticDecompression = DecompressionMethods.None;
        public const bool DefaultAutomaticRedirection = true;
        public const bool DefaultUseCookies = true;
        public const bool DefaultPreAuthenticate = false;
        public const ClientCertificateOption DefaultClientCertificateOption = ClientCertificateOption.Manual;
        public const bool DefaultUseProxy = true;
        public const bool DefaultUseDefaultCredentials = false;
        public const bool DefaultCheckCertificateRevocationList = false;
        public static readonly TimeSpan DefaultPooledConnectionLifetime = Timeout.InfiniteTimeSpan;
        public static readonly TimeSpan DefaultPooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan DefaultExpect100ContinueTimeout = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan DefaultConnectTimeout = Timeout.InfiniteTimeSpan;
    }
}
