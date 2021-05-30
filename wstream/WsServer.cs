using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace wstream
{
    public class WsServer : IDisposable
    {
        public bool IsListening { get; private set; }
        private CancellationTokenSource _stopSource;
        private IWebHost _host;

        /// <summary>
        /// Starts listening for new Websocket stream connections
        /// </summary>
        /// <param name="endpoint">The endpoint to listen to</param>
        /// <param name="connectionAdded">A connection delegate that is called when a new client is connected</param>
        /// <param name="defaultPage">Default response to clients like browsers</param>
        /// <returns></returns>
        public Task StartAsync(IPEndPoint endpoint, Func<WsStream, Task> connectionAdded, string defaultPage = Config.Version)
        {
            _stopSource = new CancellationTokenSource();
            IsListening = true;
            _host = new WebHostBuilder()
                .ConfigureLogging(x =>
                {
                    x.ClearProviders();
                })
                .UseKestrel(o =>
                {
                    o.Listen(endpoint);
                })
                .Configure(app =>
                {
                    app.UseWebSockets();
                    app.Use(async (context, next) =>
                    {
                        try
                        {
                            if (context.Request.Path == "/")
                            {
                                if (context.WebSockets.IsWebSocketRequest)
                                {
                                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                    var sId = Guid.NewGuid();
                                    await webSocket.SendAsync(new ArraySegment<byte>(sId.ToByteArray()), WebSocketMessageType.Binary, true, CancellationToken.None);
                                    var wsc = new WsStream(new WStreamBaseSocket(webSocket), sId);
                                    // dont block the current task
#pragma warning disable 4014
                                    Task.Run(()=>connectionAdded.Invoke(wsc));
#pragma warning restore 4014
                                    var ct = _stopSource.Token;
                                    while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                                    {
                                        await Task.Delay(100);
                                    }

                                    if (ct.IsCancellationRequested)
                                    {
                                        await wsc.CloseAsync();
                                    }
                                    else
                                    {
                                        await wsc.InternalCloseAsync(true);
                                    }
                                }
                                else
                                {
                                    await context.Response.WriteAsync(defaultPage);
                                }
                            }
                        }
                        catch
                        {
                            
                        }
                    });
                })
                .Build();
            return _host.StartAsync();
        }
        /// <summary>
        /// Stops the server, and disposes any resources
        /// </summary>
        public void Dispose()
        {
            if (IsListening)
            {
                IsListening = false;
                _stopSource.Cancel();
                _host.StopAsync().GetAwaiter().GetResult();
                _stopSource?.Dispose();
                _host?.Dispose();
            }
        }
    }
}