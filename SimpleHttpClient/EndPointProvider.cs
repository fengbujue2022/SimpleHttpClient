using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace SimpleHttpClient
{
    public class EndPointProvider
    {
        public virtual EndPoint GetEndPoint(string host,int port)
        {
            return new DnsEndPoint(host, port); ;
        }

        public virtual string GetHost(string host)
        {
            return host;
        }
    }
}
