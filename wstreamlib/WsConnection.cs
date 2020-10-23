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
    public class WsConnection : Stream
    {
        public readonly Guid ConnectionId;
        internal WebSocketImplementation Socket;
        public bool Connected { get; private set; }
        public delegate void ConnectionCloseDelegate(WsConnection connection);
        public event ConnectionCloseDelegate ConnectionClosedEvent;
        public readonly Socket UnderlyingSocket;
        public X509Certificate ServerCertificate;
        public EndPoint RemoteEndPoint;

        public override bool CanRead => Socket.State == WebSocketState.Open;
        public override bool CanSeek => false;
        public override bool CanWrite => Socket.State == WebSocketState.Open;

        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

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

        public override void Close()
        {
            if (Connected)
            {
                Connected = false;
                Socket.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None).Wait();
                Socket.Dispose();
                Dispose();
            }
        }
        protected override void Dispose(bool state)
        {
            Socket?.Dispose();
        }

        /// <summary>
        /// This function is useless, all data is directly sent, there is no buffer
        /// </summary>
        public override void Flush()
        {
            
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(new ArraySegment<byte>(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(new ArraySegment<byte>(buffer, offset, count));
        }
    }
}
