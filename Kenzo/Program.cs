using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProxyKit;

namespace Kenzo
{
    class Program
    {
        private static IWebHost host;

        static void Main(string[] args)
        {
            host = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(AppDomain.CurrentDomain.SetupInformation.ApplicationBase)
                .ConfigureServices(services => services.AddProxy())
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(80,
                        listenOptions => { listenOptions.Protocols = HttpProtocols.Http1AndHttp2; });
                })

                .Configure(app =>
                {
                    app.Map("", svr =>
                    {
                        svr.RunProxy(async context =>
                        {
                            var response = new HttpResponseMessage(HttpStatusCode.OK);
                            try
                            {
                                if (context.Request.Host == new HostString("mili.xuan"))
                                    response = await context.ForwardTo("https://milione.cc/").Send();
                                else
                                    response = await context.ForwardTo("https://mili.one/").Send();

                                if (response.Content.Headers.ContentType.MediaType != "text/html") return response;
                                var rewrittenResponse = await response.ReplaceContent(async upstreamContent =>
                                {
                                    string body;
                                    var steam = await upstreamContent.ReadAsByteArrayAsync();
                                    try
                                    {
                                        body = new StreamReader(new GZipStream(new MemoryStream(steam),
                                            CompressionMode.Decompress), Encoding.UTF8).ReadToEnd();
                                    }
                                    catch (Exception)
                                    {
                                        body = new StreamReader(new MemoryStream(steam), Encoding.UTF8).ReadToEnd();
                                    }

                                    return new StringContent(body.Replace("https://milione.cc/", "/"),
                                        Encoding.UTF8, "text/html");
                                });
                                context.Response.RegisterForDispose(response);
                                return rewrittenResponse;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                return response;
                            }
                        });
                    });
                }).Build();

            host.Run();
        }
    }

    public delegate Task<HttpContent> RewriteContent(HttpContent upstreamContent);

    public static class HttpResponseExtensions
    {
        public static async Task<HttpResponseMessage> ReplaceContent(
            this HttpResponseMessage upstreamResponse, RewriteContent rewriteContent)
        {
            var response = new HttpResponseMessage();
            foreach (var (key, value) in upstreamResponse.Headers) response.Headers.Add(key, value);
            response.Content = await rewriteContent(upstreamResponse.Content);
            return response;
        }
    }
}
