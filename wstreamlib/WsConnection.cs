using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using wstreamlib.Ninja.WebSockets.Internal;

namespace wstreamlib
{
    public class WsConnection : IDisposable
    {
        public readonly Guid ConnectionId;
        internal WebSocketImplementation Socket;
        public bool Connected { get; private set; }
        public delegate void ConnectionCloseDelegate(WsConnection connection);
        public event ConnectionCloseDelegate ConnectionClosedEvent;
        public readonly Socket UnderlyingSocket;
        public X509Certificate ServerCertificate;
        public EndPoint RemoteEndPoint;

        public WsConnection(WebSocket wsock, Socket underlyingSocket, X509Certificate cert)
        {
            UnderlyingSocket = underlyingSocket;
            Connected = true;
            Socket = (WebSocketImplementation) wsock;
            ConnectionId = Socket._guid;
            ServerCertificate = cert;
            Socket.ConnectionClose = ConnectionClose;
            RemoteEndPoint = underlyingSocket.RemoteEndPoint;
        }

        private void ConnectionClose()
        {
            Connected = false;
            ConnectionClosedEvent?.Invoke(this);
            Dispose();
        }

        public int Read(ArraySegment<byte> buf)
        {
            var val = Socket.Receive(buf, CancellationToken.None);
            if (val.CloseStatus.HasValue)
            {
                return 0;
            }
            return val.Count;
        }
        public void Write(ArraySegment<byte> buf)
        {
            Socket.Send(buf, WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        public void Write(ArraySegment<byte> buf, CancellationToken cancellation)
        {
            Socket.Send(buf, WebSocketMessageType.Binary, true, cancellation);
        }

        public void Close()
        {
            if (Connected)
            {
                Connected = false;
                Socket.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None).ConfigureAwait(false);
                Socket.Dispose();
                Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool state)
        {
            Socket?.Dispose();
        }
    }
}
