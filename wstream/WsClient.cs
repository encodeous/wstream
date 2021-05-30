using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace wstream
{
    /// <summary>
    /// Websocket Stream Client
    /// </summary>
    public class WsClient : IDisposable
    {
        private ClientWebSocket _client;
        private WsStream _connection;
        /// <summary>
        /// Creates a new Websocket Stream Client
        /// </summary>
        public WsClient()
        {
            _client = new ClientWebSocket();
        }

        /// <summary>
        /// Connect to the websocket server
        /// </summary>
        /// <param name="uri">The uri of the server</param>
        /// <returns>A new websocket connection</returns>
        /// <exception cref="EndOfStreamException">Thrown when the websocket gets disconnected while initializing</exception>
        public async Task<WsStream> ConnectAsync(Uri uri)
        {
            await _client.ConnectAsync(uri, CancellationToken.None);
            var bytes = new byte[16];
            int lenRead = 0;
            while (lenRead < 16)
            {
                var res = await _client.ReceiveAsync(
                    new ArraySegment<byte>(bytes, lenRead, 16 - lenRead), CancellationToken.None);
                if (res.Count == 0) throw new EndOfStreamException("Unexpected end of Websocket while initializing");
                lenRead += res.Count;
            }
            return _connection = new WsStream(new WStreamBaseSocket(_client), new Guid(bytes));
        }
        /// <summary>
        /// Stops the websocket stream client, and disposes any underlying native resources
        /// </summary>
        public void Dispose()
        {
            _connection?.Dispose();
            _client?.Dispose();
        }
    }
}