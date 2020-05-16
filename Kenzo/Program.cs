using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        private static IWebHost Host;
        public static Dictionary<HostString, Uri> HostDictionary = new Dictionary<HostString, Uri>();

        static void Main(string[] args)
        {
            HostDictionary.Add(new HostString("mili.xuan"), new Uri("https://milione.cc/"));
            Host = new WebHostBuilder()
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
                            var response = new HttpResponseMessage();
                            try
                            {
                                if (HostDictionary.TryGetValue(context.Request.Host,out var fwdToUri))
                                {
                                    response = await context.ForwardTo(fwdToUri).Send();

                                    if (response.Content.Headers.ContentType == null
                                        || response.Content.Headers.ContentType.MediaType != "text/html")
                                        return response;

                                    var reResponse = await response.ReplaceContent(async upContent =>
                                    {
                                        string body;
                                        var steam = await upContent.ReadAsByteArrayAsync();
                                        try
                                        {
                                            body = new StreamReader(new GZipStream(new MemoryStream(steam),
                                                CompressionMode.Decompress), Encoding.UTF8).ReadToEnd();
                                        }
                                        catch (Exception)
                                        {
                                            body = new StreamReader(new MemoryStream(steam), Encoding.UTF8).ReadToEnd();
                                        }

                                        return new StringContent(body.Replace(fwdToUri.ToString(), "/"),
                                            Encoding.UTF8, response.Content.Headers.ContentType.MediaType);
                                    });
                                    context.Response.RegisterForDispose(response);
                                    context.Response.Headers.Add("x-forwarder-by", "KENZO/Zero");
                                    return reResponse;
                                }

                                response = await context.ForwardTo("https://mili.one/").Send();
                                return response;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                return response;
                            }
                        });
                    });
                }).Build();

            Host.Run();
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
