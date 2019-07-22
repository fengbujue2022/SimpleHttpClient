using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace SimpleHttpClient
{
    internal class HttpConnectionKey : IEquatable<HttpConnectionKey>
    {
        public readonly HttpConnectionKind Kind;
        public readonly string Host;
        public readonly int Port;
        public readonly string SslHostName;

        public HttpConnectionKey(HttpConnectionKind kind, string host, int port, string sslHostName)
        {
            Kind = kind;
            Host = host;
            Port = port;
            SslHostName = sslHostName;
        }

        public bool Equals(HttpConnectionKey other)
        {
            return other.Kind == Kind
                 &&
                 other.Host == Host
                 &&
                 other.Port == Port
                 &&
                 other.SslHostName == SslHostName;
        }

        public static implicit operator HttpConnectionKey(HttpRequestMessage httpRequest)
        {
            HttpConnectionKind kind;
            if (httpRequest.RequestUri.Scheme.ToLower().Equals("https"))
            {
                kind = HttpConnectionKind.Https;
            }
            else
            {
                kind = HttpConnectionKind.Http;
            }

            return new HttpConnectionKey(kind, httpRequest.RequestUri.IdnHost, httpRequest.RequestUri.Port, httpRequest.RequestUri.IdnHost);
        }
    }
}
