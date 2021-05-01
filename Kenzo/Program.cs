﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Runtime.Caching;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using ProxyKit;
using static System.Runtime.Caching.MemoryCache;

namespace Kenzo
{
    class Program
    {
        private static IWebHost Host;
        public static Dictionary<HostString, Uri> HostDictionary = new Dictionary<HostString, Uri>();
        public static Dictionary<HostString, X509Certificate2> CertDictionary = new Dictionary<HostString, X509Certificate2>();

        static void Main(string[] args)
        {
            HostDictionary.Add(new HostString("mili.xuan"), new Uri("https://milione.cc/"));
            //HostDictionary.Add(new HostString("milione.xuan"), new Uri("http://127.0.0.1:2020/"));
            //HostDictionary.Add(new HostString("xuan"), new Uri("https://mili.one/"));
            Host = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(AppDomain.CurrentDomain.SetupInformation.ApplicationBase)
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddProxy(httpClientBuilder =>
                        httpClientBuilder.ConfigureHttpClient(client =>
                            client.Timeout = TimeSpan.FromSeconds(5)));
                })
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(80,
                        listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                            listenOptions.UseHttps(httpsOptions =>
                            {

                                httpsOptions.ServerCertificateSelector = (connectionContext, name) =>
                                    name != null && CertDictionary.TryGetValue(new HostString(name), out var cert)
                                        ? cert
                                        : CertDictionary[new HostString("notfound.xuan")];
                            });
                        });
                })

                .Configure(app =>
                {
                    app.Map("/put", svr =>
                        app.UseRouting().UseEndpoints(endpoint =>
                        {
                            endpoint.Map("/put/list", async context =>
                                {
                                    await context.Response.WriteAsync(string.Join(Environment.NewLine,
                                        HostDictionary.Keys.ToArray()));
                                });
                            endpoint.Map("/put", async context =>
                            {
                                var queryDictionary = context.Request.Query;
                                var p = queryDictionary.TryGetValue("p", out var pStr) ? pStr.ToString() : "http";
                                HostDictionary.Add(new HostString(queryDictionary["host"]),
                                    new Uri($"{p}://{queryDictionary["source"]}/"));
                                await context.Response.WriteAsync("OK");
                            });
                            endpoint.Map("/put/txt", async context =>
                            {
                                var queryDictionary = context.Request.Query;
                                HostDictionary.Add(new HostString(queryDictionary["name"]),
                                    new Uri(queryDictionary["txt"].ToString().Split('@')[1]));
                                await context.Response.WriteAsync("OK2");
                            });
                        }));
                    app.Map(string.Empty, svr =>
                    {
                        svr.RunProxy(async context =>
                        {
                            var response = new HttpResponseMessage();
                            try
                            {

                                if (HostDictionary.TryGetValue(context.Request.Host, out var fwdToUri))
                                {
                                    if (IsLocalHost(fwdToUri.Host))
                                        response = await context.ForwardTo(IsLocalPortUse(fwdToUri.Port)
                                            ? fwdToUri
                                            : new Uri("https://mili.one/SiteNotFound/")).Send();
                                    else
                                        response = await context.ForwardTo(IsTcportUse(fwdToUri.Host, fwdToUri.Port)
                                            ? fwdToUri
                                            : new Uri("https://mili.one/SiteNotFound/")).Send();

                                    response.Headers.Add("X-Forwarder-By", "KENZO/Zero");
                                    var statusCode = Convert.ToInt32(response.StatusCode);
                                    if ((statusCode >= 400 || statusCode < 200)
                                        || response.Content.Headers.ContentType == null
                                        || response.Content.Headers.ContentType.MediaType != "text/html")
                                        return response;

                                    var reResponse = await response.ReplaceContent(async upContent =>
                                    {
                                        var body = await GetBody(upContent);
                                        return new StringContent(body.Replace(fwdToUri.ToString(), "/"),
                                            Encoding.UTF8, response.Content.Headers.ContentType.MediaType);
                                    });
                                    context.Response.RegisterForDispose(response);
                                    return reResponse;
                                }
                                if (context.Request.Host.ToString().Contains(".at."))
                                {
                                    var hostSplit = context.Request.Host.ToString().Split(".at.");
                                    var intent = new Uri("http://" + hostSplit[0].Split('-')[0]);
                                    if (context.Request.Host.ToString().Contains("-"))
                                        intent = new Uri(
                                            "http://" + hostSplit[0].Split('-')[0] + ":" + hostSplit[0].Split('-')[1]);
                                    response = await context.ForwardTo(
                                        //HostDictionary.TryGetValue(new HostString(hostSplit[1]), out _) &&
                                        IsTcportUse(intent.Host, intent.Port)
                                            ? intent
                                            : new Uri("https://mili.one/SiteNotFound/")).Send();
                                }
                                else
                                {
                                    response = await context.ForwardTo("https://mili.one/SiteNotFound/").Send();
                                }
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

        public static bool IsLocalHost(string host) =>
            IPAddress.TryParse(host, out var ipAddress)
                ? IPAddress.IsLoopback(ipAddress)
                : host.ToLower().Equals("localhost");

        public static bool IsLocalPortUse(int port) =>
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(endPoint => endPoint.Port == port);

        public static bool IsTcportUse(string ip, int port)
        {
            var point = IPAddress.TryParse(ip, out var ipAddress)
                ? new IPEndPoint(ipAddress, port)
                : new IPEndPoint(Dns.GetHostAddresses(ip)[0], port);
            if (Default.Contains(point.ToString())) return (bool) Default.Get(point.ToString());
            var socks = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                { Blocking = true, ReceiveTimeout = 3000, SendTimeout = 3000 };
            try
            {
                socks.Connect(point);
                socks.Close(500);
                AddCachePoint(point, true);
                return true;
            }
            catch
            {
                AddCachePoint(point,false);
                return false;
            }
        }

        public static void AddCachePoint(IPEndPoint keyPoint, bool value)
        {
            if (!Default.Contains(keyPoint.ToString()))
                Task.Run(() => Default.Add(keyPoint.ToString(), value, new CacheItemPolicy
                {
                    AbsoluteExpiration =
                        DateTimeOffset.Now + TimeSpan.FromMinutes(5)
                }));
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
    }
}
