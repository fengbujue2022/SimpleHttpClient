using System;
using System.Net.Http;
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
                var client = new SimpleHttpClient.HttpClient();

                var request = new HttpRequestMessage(HttpMethod.Get, "http://staging-ap-api.openrice.com.cn");

                var res = await client.SendAsync(request);
            }
            catch (Exception ex)
            {

            }
        }
    }
}
