using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace wstream
{
    /// <summary>
    /// Base implementation of WStreamSocket
    /// </summary>
    internal class WStreamBaseSocket : WStreamSocket
    {
        private WebSocket _socket;
        public WStreamBaseSocket(WebSocket underlyingSocket)
        {
            _socket = underlyingSocket;
        }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            return _socket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        }

        public override void Dispose()
        {
            _socket.Dispose();
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return _socket.ReceiveAsync(buffer, cancellationToken);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
            CancellationToken cancellationToken)
        {
            return _socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
        }
    }
}