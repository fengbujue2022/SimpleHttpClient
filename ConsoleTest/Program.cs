using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SimpleHttpClient;

namespace ConsoleTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var client = HttpClientFactory.Create(new HeaderValueHandler());

                Parallel.ForEach(Enumerable.Range(0, 10), (index) =>
                {
                    Task.Run(async () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://ss1.bdstatic.com/5eN1bjq8AAUYm2zgoY3K/r/www/cache/static/protocol/https/amd_modules/@baidu/search-sug_73a0f48.js");
                        var res = await client.SendAsync(request);
                    }).Wait();
                });
            }
            catch (Exception ex)
            {

            }
        }
    }

    internal class HeaderValueHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            request.Headers.Add("User-Agent", "JOJO");

            return base.SendAsync(request, cancellationToken);
        }
    }


}
