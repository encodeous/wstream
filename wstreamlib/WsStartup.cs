using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace wstreamlib
{
    class WsStartup
    {
        private WStreamServer _server;
        public WsStartup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            _server = (WStreamServer)app.ApplicationServices.GetService(typeof(WStreamServer));
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });
            app.UseRouting();
            app.UseWebSockets();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/",
                    async context => {
                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            var ws = await context.WebSockets.AcceptWebSocketAsync();
                            var wsc = new WsConnection(ws);
                            _server.AddConnection(wsc);
                            while (ws.State == WebSocketState.Open)
                            {
                                await Task.Delay(100);
                            }
                            _server.ConnectionClosed(wsc);
                        }
                        else
                        {
                            await context.Response.WriteAsync(Config.Version);
                        }
                    });
            });
        }
    }
}
