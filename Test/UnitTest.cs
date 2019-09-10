using NUnit.Framework;
using SimpleHttpClient;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace Test
{
    [TestFixture]
    public class UnitTest
    {
        [Test]
        public async Task PipelineHandlerTest()
        {
            var simpleClient = SimpleHttpClient.HttpClientFactory.Create(new HeaderValueHandler());
            var response = await simpleClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://www.baidu.com"));
            Assert.IsTrue(response.RequestMessage.Headers.Contains("User-Agent"));
            Assert.AreEqual(response.RequestMessage.Headers.UserAgent.Count, 1);
            Assert.AreEqual(response.RequestMessage.Headers.UserAgent.First().ToString(), "JOJO");
        }

        [Test]
        public async Task ReseponseParseTest()
        {
            var client = new System.Net.Http.HttpClient();
            var simpleClient = new SimpleHttpClient.HttpClient();

            var clientResponse = await SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://ss1.bdstatic.com/5eN1bjq8AAUYm2zgoY3K/r/www/cache/static/protocol/https/amd_modules/@baidu/search-sug_73a0f48.js")
                , client);

            var simpleClientResponse = await SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://ss1.bdstatic.com/5eN1bjq8AAUYm2zgoY3K/r/www/cache/static/protocol/https/amd_modules/@baidu/search-sug_73a0f48.js")
                , simpleClient);
            var d = await simpleClientResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(
                 clientResponse.StatusCode,
                 simpleClientResponse.StatusCode);

            Assert.AreEqual(
                 clientResponse.ReasonPhrase,
                 simpleClientResponse.ReasonPhrase);

            Assert.AreEqual(
                 clientResponse.Version,
                 simpleClientResponse.Version);

            //TODO: to know why 'Ohc-Cache-HIT' is different
            var headerToSkip = new string[] { "Date", "Age", "Ohc-Cache-HIT", "Ohc-Response-Time" };
            var simpleHeaderEnumerator = simpleClientResponse.Headers.GetEnumerator();
            foreach (var header in clientResponse.Headers)
            {
                simpleHeaderEnumerator.MoveNext();
                if (headerToSkip.Contains(header.Key))
                    continue;

                Assert.AreEqual(header, simpleHeaderEnumerator.Current);
            }

            var simpleContentHeaderEnumerator = simpleClientResponse.Headers.GetEnumerator();
            foreach (var contentHeader in clientResponse.Headers)
            {
                simpleContentHeaderEnumerator.MoveNext();
                if (headerToSkip.Contains(contentHeader.Key))
                    continue;
                Assert.AreEqual(contentHeader, simpleContentHeaderEnumerator.Current);
            }

            Assert.AreEqual(
                   await clientResponse.Content.ReadAsStringAsync(),
                   await simpleClientResponse.Content.ReadAsStringAsync());
        }


        [Test]
        public async Task Startup()
        {
            //https://i.pximg.net/img-master/img/2017/07/08/22/38/22/63771031_p0_master1200.jpg
            var simpleClient = SimpleHttpClient.HttpClientFactory.Create(new HeaderValueHandler());
            var response = await simpleClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://i.pximg.net/img-master/img/2017/07/08/22/38/22/63771031_p0_master1200.jpg"));
            //var d = await response.Content.ReadAsStringAsync();
            using (var s = File.Open($@"D:\{"63771031_p0_master1200.jpg"}", FileMode.OpenOrCreate))
            {
                await response.Content.CopyToAsync(s);
            }
        }

        private static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpMessageInvoker httpMessageInvoker)
        {
            return httpMessageInvoker.SendAsync(request, CancellationToken.None);
        }




        private class HeaderValueHandler : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                request.Headers.Add("User-Agent", "PixivIOSApp/5.8.0");
                request.Headers.Add("referer", "https://app-api.pixiv.net/");
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
