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
        public readonly Uri ProxyUri;

        public HttpConnectionKey(HttpConnectionKind kind, string host, int port, string sslHostName, Uri proxyUri)
        {
            Kind = kind;
            Host = host;
            Port = port;
            SslHostName = sslHostName;
            ProxyUri = proxyUri;
        }

        public override int GetHashCode() =>
                (SslHostName == Host ?
                    HashCode.Combine(Kind, Host, Port, ProxyUri) :
                    HashCode.Combine(Kind, Host, Port, SslHostName, ProxyUri));

        public override bool Equals(object obj) =>
            obj != null &&
            obj is HttpConnectionKey &&
            Equals((HttpConnectionKey)obj);

        public bool Equals(HttpConnectionKey other) =>
            Kind == other.Kind &&
            Host == other.Host &&
            Port == other.Port &&
            ProxyUri == other.ProxyUri &&
            SslHostName == other.SslHostName;
    }
}
