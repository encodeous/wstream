using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace wstream
{
    /// <summary>
    /// Wrapper ontop of .NET's WebSocket
    /// </summary>
    public abstract class WStreamSocket
    {
        public abstract Task CloseAsync(WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken);
        public abstract void Dispose();
        public abstract Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken);
        public abstract Task SendAsync(ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken);
    }
}