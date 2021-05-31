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
    public class WsStream : Stream, IDisposable
    {
        /// <summary>
        /// Client Id, sent by the server to the client
        /// </summary>
        public readonly Guid ConnectionId;
        /// <summary>
        /// Checks if the websocket is connected
        /// </summary>
        public bool Connected { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream">Websocket Instance</param>
        /// <param name="remote">true if the disconnection is from the remote</param>
        public delegate void ConnectionCloseDelegate(WsStream stream, bool remote);
        
        /// <summary>
        /// Called when this connection is closed
        /// </summary>
        public event ConnectionCloseDelegate ConnectionClosedEvent;
        private CancellationTokenSource _cancellationTokenSource;

        private WStreamSocket _socket;
        
        internal WsStream(WStreamSocket webSocket, Guid id)
        {
            ConnectionId = id;
            _cancellationTokenSource = new CancellationTokenSource();
            _socket = webSocket;
            Connected = true;
        }

        internal async Task InternalCloseAsync(bool remote = false)
        {
            if (Connected)
            {
                Connected = false;
                _cancellationTokenSource.Cancel();
                ConnectionClosedEvent?.Invoke(this, remote);
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch
                {
                    
                }
                _socket.Dispose();
            }
        }
        
        /// <summary>
        /// Closes the websocket
        /// </summary>
        /// <returns></returns>
        public Task CloseAsync()
        {
            return InternalCloseAsync();
        }
        
        private Task Write(ArraySegment<byte> arr, CancellationToken ct)
        {
            return _socket.SendAsync(arr, WebSocketMessageType.Binary, true, ct);
        }
        
        private async Task<int> Read(ArraySegment<byte> arr, CancellationToken ct)
        {
            var res = await _socket.ReceiveAsync(arr, ct);
            if (res.Count == 0)
            {
                try
                {
                    await CloseAsync();
                }
                catch
                {
                    
                }
                return 0;
            }
            return res.Count;
        }
        
        /// <summary>
        /// Reads data into the buffer
        /// </summary>
        /// <param name="buffer">destination buffer</param>
        /// <param name="cancellationToken"></param>
        /// <returns>the number of bytes read, 0 if the connection has closed</returns>
        public Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            return Read(buffer, cancellationToken);
        }
        /// <summary>
        /// Write the buffer to the websocket
        /// </summary>
        /// <param name="buffer">the source buffer</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task WriteAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            return Write(buffer, cancellationToken);
        }

        /// <summary>
        /// Wraps the underlying socket with a new layer
        /// </summary>
        /// <param name="wrapCallback">Create a new subclass of WStreamSocket that wraps around the current socket and return it</param>
        public async Task WrapSocketAsync(Func<WStreamSocket, Task<WStreamSocket>> wrapCallback)
        {
            _socket = await wrapCallback(_socket);
        }

        #region Stream Overrides

        /// <summary>
        /// Doesn't do anything
        /// </summary>
        public override void Flush()
        {
            // doesnt do anything
        }

        public override void Close()
        {
            InternalCloseAsync().GetAwaiter().GetResult();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var tsk = Read(new ArraySegment<byte>(buffer, offset, count), CancellationToken.None);
            return tsk.GetAwaiter().GetResult();
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Read(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Write(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            var tsk = Write(new ArraySegment<byte>(buffer, offset, count), CancellationToken.None);
            tsk.GetAwaiter().GetResult();
        }

        public override bool CanRead => Connected;
        public override bool CanSeek => false;
        public override bool CanWrite => Connected;

        #endregion
        #region Unsupported

        /// <summary>
        /// Not supported
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="origin"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException("Websocket does not support seeking!"); }
        /// <summary>
        /// Not supported
        /// </summary>
        /// <param name="value"></param>
        /// <exception cref="NotSupportedException"></exception>
        public override void SetLength(long value) { throw new NotSupportedException("Websocket does not support seeking!"); }
        /// <summary>
        /// Not supported
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override long Length => throw new NotSupportedException("Websocket does not support seeking!");

        /// <summary>
        /// Not supported
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override long Position
        {
            get => throw new NotSupportedException("Websocket does not support seeking!");
            set => throw new NotSupportedException("Websocket does not support seeking!");
        }

        #endregion

    }
}
