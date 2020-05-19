using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
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
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddProxy();
                })
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(80,
                        listenOptions => { listenOptions.Protocols = HttpProtocols.Http1AndHttp2; });
                })

                .Configure(app =>
                {
                    app.Map("/add", svr =>
                        app.UseRouting().UseEndpoints(endpoint =>
                            endpoint.Map("/add", async context =>
                            {
                                var queryDictionary = context.Request.Query;
                                var p = queryDictionary.TryGetValue("p", out var pStr) ? pStr.ToString() : "http";
                                HostDictionary.Add(new HostString(queryDictionary["host"]),
                                    new Uri($"{p}://{queryDictionary["source"]}/"));
                                await context.Response.WriteAsync("OK");
                            })));
                    app.Map("", svr =>
                    {
                        svr.RunProxy(async context =>
                        {
                            var response = new HttpResponseMessage();
                            try
                            {
                                if (HostDictionary.TryGetValue(context.Request.Host,out var fwdToUri))
                                {
                                    if (IsLocalHost(fwdToUri.Host))
                                    {
                                        if (IsLocalPortUse(fwdToUri.Port))
                                            response = await context.ForwardTo(fwdToUri).Send();
                                        else
                                            response = await context.ForwardTo("https://mili.one/SiteNotFound/").Send();
                                    }
                                    else
                                    {
                                        response = await context.ForwardTo(fwdToUri).Send();
                                    }

                                    if (response.Content.Headers.ContentType == null
                                        || response.Content.Headers.ContentType.MediaType != "text/html")
                                        return response;

                                    var reResponse = await response.ReplaceContent(async upContent =>
                                    {
                                        var body = await GetBody(upContent);
                                        return new StringContent(body.Replace(fwdToUri.ToString(), "/"),
                                            Encoding.UTF8, response.Content.Headers.ContentType.MediaType);
                                    });
                                    context.Response.RegisterForDispose(response);
                                    context.Response.Headers.Add("x-forwarder-by", "KENZO/Zero");
                                    return reResponse;
                                }

                                response = await context.ForwardTo("https://mili.one/SiteNotFound/").Send();
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

        public static bool IsLocalHost(string host)
        {
            return IPAddress.TryParse(host, out var ipAddress)
                ? IPAddress.IsLoopback(ipAddress)
                : host.ToLower().Equals("localhost");
        }

        public static async Task<string> GetBody(HttpContent content)
        {
            var bytes = await content.ReadAsByteArrayAsync();
            if ((bytes[0] == 31 && bytes[1] == 139) ||
                (content.Headers.TryGetValues("content-encoding", out var codeValues)
                 && codeValues.Contains("gzip")))
                return new StreamReader(new GZipStream(new MemoryStream(bytes),
                    CompressionMode.Decompress), Encoding.UTF8).ReadToEnd();
            return new StreamReader(new MemoryStream(bytes), Encoding.UTF8).ReadToEnd();
        }

        public static bool IsLocalPortUse(int port)
        {
            IPEndPoint[] ipEndPointsTcp = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            IPEndPoint[] ipEndPointsUdp = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();

            return ipEndPointsTcp.Any(endPoint => endPoint.Port == port)
                   || ipEndPointsUdp.Any(endPoint => endPoint.Port == port);
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
