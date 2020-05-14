using System;
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
        private static IWebHost host;
        static void Main(string[] args)
        {
            host = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(AppDomain.CurrentDomain.SetupInformation.ApplicationBase)
                .ConfigureServices(services =>
                {
                    //services.AddRouting();
                    services.AddProxy();
                })
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(80, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    });
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
                            if (context.Request.Host == new HostString("milione.xuan"))
                            {
                                var fwdContext = context
                                    .ForwardTo("https://mili.one/")
                                    .CopyXForwardedHeaders();
                                return await fwdContext.Send();
                            }
                            else
                            {
                                var fwdContext = context
                                    .ForwardTo("https://milione.cc/")
                                    .CopyXForwardedHeaders();
                                return await fwdContext.Send();
                            }
                        });
                    });
                })
                .Build();

            host.Run();
        }
    }
}
