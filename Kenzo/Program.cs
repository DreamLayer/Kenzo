using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
                    //app.UseRouting().UseEndpoints(endpoint =>
                    //    endpoint.Map("/", async context => 
                    //        await context.Response.WriteAsync("Welcome to Kenzo ProxyKit")));
                    app.Map("", svr =>
                    {
                        svr.RunProxy(async context =>
                        {
                            var response = await context
                                .ForwardTo("https://milione.cc/")
                                .Send();

                            if (response.Content?.Headers?.ContentType.MediaType != "text/html") return response;
                            var rewrittenResponse = await response.ReplaceContent(async upstreamContent =>
                            {
                                var gZipStream = new GZipStream(await upstreamContent.ReadAsStreamAsync(),
                                    CompressionMode.Decompress);
                                var body = new StreamReader(gZipStream, Encoding.UTF8).ReadToEnd();
                                body = body.Replace("https://milione.cc/", "http://127.0.0.1/");
                                return new StringContent(body, Encoding.UTF8, "text/html");
                            });
                            context.Response.RegisterForDispose(response);
                            return rewrittenResponse;
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
