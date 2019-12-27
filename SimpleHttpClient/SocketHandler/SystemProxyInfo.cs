using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleHttpClient
{
    public class SystemProxyInfo
    {
        public static IWebProxy Proxy => s_proxy.Value;

        private static readonly Lazy<IWebProxy> s_proxy = new Lazy<IWebProxy>(ConstructSystemProxy);

        public static IWebProxy ConstructSystemProxy()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return HttpEnvironmentProxy.TryCreateWindows(out IWebProxy proxy) ? proxy : new HttpNoProxy();
            }
            else if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!HttpEnvironmentProxy.TryCreateWindows(out IWebProxy proxy))
                {
                    if (HttpWindowsProxy.TryCreate(out proxy))
                    {
                        return proxy;
                    }
                }
            }
            return new HttpNoProxy();
        }
    }
}
