using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            var simpleClient = new SimpleHttpClient.HttpClient();
            simpleClient.Timeout = TimeSpan.FromMilliseconds(1000);

            Task.Run(async () =>
            {
                try
                {
                    foreach (var i in Enumerable.Range(1, 100))
                    {
                        var r = await simpleClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:60988/test2") { Version = new Version("1.1") });
                        //var body = await r.Content.ReadAsStringAsync();
                        Console.WriteLine($"the index {i} {r.StatusCode.ToString()}");
                    }
                }
                catch (OperationCanceledException ex)
                {
                    Console.WriteLine("YOU ARE FAIL");
                }
            });


            Task.Run(async () =>
            {
                try
                {
                    foreach (var i in Enumerable.Range(1, 100))
                    {
                        var r = await simpleClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:60988/test") { Version = new Version("1.1") });
                        //var body = await r.Content.ReadAsStringAsync();
                        Console.WriteLine($"the index {i} {r.StatusCode.ToString()}");
                    }
                }
                catch (OperationCanceledException ex)
                {
                    Console.WriteLine("YOU ARE FAIL");
                }
            });

            Thread.Sleep(100000);
        }
    }
}
