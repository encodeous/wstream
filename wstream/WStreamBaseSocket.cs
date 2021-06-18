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
        internal Action CloseCallback;
        public override bool Connected => _connected;
        private bool _connected = true;
        
        public WStreamBaseSocket(WebSocket underlyingSocket, bool parity) : base(parity)
        {
            _socket = underlyingSocket;
        }
        
        public override async Task CloseAsync()
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            Dispose();
        }

        public override void Dispose()
        {
            _connected = false;
            CloseCallback();
            _socket.Dispose();
        }
        
        public override async Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            var res = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (res.CloseStatus != null)
            {
                await CloseAsync();
                return 0;
            }
            return res.Count;
        }

        public override Task WriteAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            return _socket.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken);
        }
    }
}