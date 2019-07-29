using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using SimpleHttpClient;

namespace ConsoleTest
{
    class Program
    {
        private static readonly SimpleHttpClient.HttpClient client = HttpClientFactory.Create(new HeaderValueHandler());

        static async Task Main(string[] args)
        {
            try
            {
                //await TestParallelCall(10);
                var request = new HttpRequestMessage(HttpMethod.Get, "https://ss1.bdstatic.com/5eN1bjq8AAUYm2zgoY3K/r/www/cache/static/protocol/https/amd_modules/@baidu/search-sug_73a0f48.js");
                var res = await client.SendAsync(request);
                
            }
            catch (Exception ex)
            {

            }

        }

        public static async Task TestParallelCall(int limit)
        {
            Parallel.ForEach(Enumerable.Range(0, limit), async (index) =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://ss1.bdstatic.com/5eN1bjq8AAUYm2zgoY3K/r/www/cache/static/protocol/https/amd_modules/@baidu/search-sug_73a0f48.js");
                var res = await client.SendAsync(request);
            });
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
