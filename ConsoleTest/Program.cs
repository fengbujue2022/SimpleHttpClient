using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimpleHttpClient;

namespace ConsoleTest
{
    class Program
    {
        private static readonly SimpleHttpClient.HttpClient simpleClient = HttpClientFactory.Create(new HeaderValueHandler());
        private static readonly System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();

        static async Task Main(string[] args)
        {
            try
            {
                var clientResponse = await SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, "https://ss1.bdstatic.com/5eN1bjq8AAUYm2zgoY3K/r/www/cache/static/protocol/https/amd_modules/@baidu/search-sug_73a0f48.js")
                    , client);

                var simpleClientResponse = await SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, "https://ss1.bdstatic.com/5eN1bjq8AAUYm2zgoY3K/r/www/cache/static/protocol/https/amd_modules/@baidu/search-sug_73a0f48.js")
                    , simpleClient);

                Assert.AreEqual(
                     clientResponse.StatusCode,
                     simpleClientResponse.StatusCode);

                Assert.AreEqual(
                     clientResponse.ReasonPhrase,
                     simpleClientResponse.ReasonPhrase);

                Assert.AreEqual(
                     clientResponse.Version,
                     simpleClientResponse.Version);

                Assert.AreEqual(
                     clientResponse.Headers,
                     simpleClientResponse.Headers);

                Assert.AreEqual(
                    await clientResponse.Content.ReadAsStringAsync(), 
                    await simpleClientResponse.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }


        public static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpMessageInvoker httpMessageInvoker)
        {
            return httpMessageInvoker.SendAsync(request, CancellationToken.None);
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
