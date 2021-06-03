using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace wstream
{

    /// <summary>
    /// Encapsulates a Connection between WStream and WStreamServer
    /// </summary>
    public class WsStream : WStreamSocket
    {
        /// <summary>
        /// Client Id, sent by the server to the client
        /// </summary>
        public readonly Guid ConnectionId;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream">Websocket Instance</param>
        public delegate void ConnectionCloseDelegate(WsStream stream);
        
        /// <summary>
        /// Called when this connection is closed
        /// </summary>
        public event ConnectionCloseDelegate ConnectionClosedEvent;
        private CancellationTokenSource _cancellationTokenSource;

        internal WsStream(WStreamSocket webSocket, Guid id) : base(webSocket)
        {
            ConnectionId = id;
            _cancellationTokenSource = new CancellationTokenSource();
            var bsock = (WStreamBaseSocket) webSocket;
            bsock.CloseCallback = () => InternalClose();
        }

        internal void InternalClose(bool remote = false)
        {
            _cancellationTokenSource.Cancel();
            ConnectionClosedEvent?.Invoke(this);
        }

        /// <summary>
        /// Wraps the underlying socket with a new layer
        /// </summary>
        /// <param name="wrapCallback">Create a new subclass of WStreamSocket that wraps around the current socket and return it</param>
        public async Task WrapSocketAsync(Func<WStreamSocket, Task<WStreamSocket>> wrapCallback)
        {
            SetSocket(await wrapCallback(WrappedSocket));
        }
    }
}
