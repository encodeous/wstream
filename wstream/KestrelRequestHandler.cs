using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace wstream
{
    internal class KestrelRequestHandler : IHttpApplication<HttpContext>
    {
        private WebSocketMiddleware _wsMiddleware;
        public KestrelRequestHandler(Func<WsStream, Task> connectionAdded, int bufferSize, CancellationToken stopToken,
            string defaultPage = Config.Version)
        {
            // create a raw middleware
            _wsMiddleware = new WebSocketMiddleware(async ctx =>
            {
                if (ctx.WebSockets.IsWebSocketRequest)
                {
                    WebSocket webSocket = await ctx.WebSockets.AcceptWebSocketAsync();
                    var sId = Guid.NewGuid();
                    await webSocket.SendAsync(new ArraySegment<byte>(sId.ToByteArray()), WebSocketMessageType.Binary,
                        true, CancellationToken.None);
                    var wsc = new WsStream(new WStreamBaseSocket(webSocket, true), sId);
                    // dont block the current task
#pragma warning disable 4014
                    Task.Run(() => connectionAdded.Invoke(wsc));
#pragma warning restore 4014
                    while (webSocket.State == WebSocketState.Open && !stopToken.IsCancellationRequested)
                    {
                        await Task.Delay(100);
                    }
                    
                    wsc.Close();
                }
                else
                {
                    await ctx.Response.WriteAsync(defaultPage);
                }
            }, Options.Create(new WebSocketOptions()
            {
                ReceiveBufferSize = bufferSize
            }), NullLoggerFactory.Instance);
        }

        public HttpContext CreateContext(IFeatureCollection contextFeatures)
        {
            return new DefaultHttpContext(contextFeatures);
        }

        public Task ProcessRequestAsync(HttpContext context)
        {
            return _wsMiddleware.Invoke(context);
        }

        public void DisposeContext(HttpContext context, Exception exception)
        {
            
        }
    }
}