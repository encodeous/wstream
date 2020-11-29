using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace wstreamlib
{
    public class WStream
    {
        public WStream()
        {
        }

        private ClientWebSocket _client;
        public async Task<WsConnection> Connect(Uri uri)
        {
            _client = new ClientWebSocket();
            await _client.ConnectAsync(uri, CancellationToken.None);
            return new WsConnection(_client);
        }

        public async Task Close()
        {
            await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
    }
}