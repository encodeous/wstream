using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
        public string[] ListeningAddresses { get; private set; }
        private CancellationTokenSource _stopSource;
        private KestrelServer _server;

        /// <summary>
        /// Starts listening for new Websocket stream connections
        /// </summary>
        /// <param name="endpoint">The endpoint to listen to</param>
        /// <param name="connectionAdded">A connection delegate that is called when a new client is connected</param>
        /// <param name="bufferSize">Buffer sized used for receiving</param>
        /// <param name="defaultPage">Default response to clients like browsers</param>
        /// <returns></returns>
        public Task StartAsync(IPEndPoint endpoint, Func<WsStream, Task> connectionAdded, int bufferSize = Config.InternalBufferSize, string defaultPage = Config.Version)
        {
            if (IsListening) throw new InvalidOperationException("WsServer is already running!");
            _stopSource = new CancellationTokenSource();
            IsListening = true;
            // setup kestrel parameters
            var logger = new NullLoggerFactory();
            var kestrelOptions = new KestrelServerOptions();
            var lifetime = new ApplicationLifetime();
            var socketTransportFactory = new SocketTransportFactory(Options.Create(new SocketTransportOptions()), lifetime, logger);
            // start kestrel
            _server = new KestrelServer(Options.Create(kestrelOptions), socketTransportFactory, logger);
            _server.Options.Listen(endpoint);
            return _server
                .StartAsync(new KestrelRequestHandler(connectionAdded, bufferSize, _stopSource.Token, defaultPage),
                    CancellationToken.None).ContinueWith(
                    x =>
                    {
                        var addr = _server.Features.Get<IServerAddressesFeature>();
                        ListeningAddresses = addr.Addresses.ToArray();
                    });
        }

        /// <summary>
        /// Shuts down the server
        /// </summary>
        public async Task StopAsync()
        {
            if (IsListening)
            {
                IsListening = false;
                _stopSource.Cancel();
                var cts = new CancellationTokenSource(500);
                await _server.StopAsync(cts.Token);
                _stopSource.Dispose();
                _server.Dispose();
            }
        }
        /// <summary>
        /// Stops the server, and disposes any resources
        /// </summary>
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }
}