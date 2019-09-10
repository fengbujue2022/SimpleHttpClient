using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ConsoleTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            //https://i.pximg.net/img-master/img/2017/07/08/22/38/22/63771031_p0_master1200.jpg
            var simpleClient = SimpleHttpClient.HttpClientFactory.Create(new HeaderValueHandler());
            var response = await simpleClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://i.pximg.net/img-master/img/2017/07/08/22/38/22/63771031_p0_master1200.jpg"));
            //var d = await response.Content.ReadAsStringAsync();
            using (var s = File.Open($@"D:\63771031_p0_master1200.jpg", FileMode.OpenOrCreate))
            {
                await response.Content.CopyToAsync(s);
            }
        }
    }

    public class HeaderValueHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            request.Headers.Add("User-Agent", "PixivIOSApp/5.8.0");
            request.Headers.Add("referer", "https://app-api.pixiv.net/");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
