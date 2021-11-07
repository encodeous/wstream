using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace wstream
{
    /// <summary>
    /// Wrapper ontop of .NET's WebSocket
    /// </summary>
    public abstract class WStreamSocket : Stream
    {
        /// <summary>
        /// Underlying Socket
        /// </summary>
        protected WStreamSocket WrappedSocket { get; private set; }
        /// <summary>
        /// True if current connection is on the server side
        /// </summary>
        public readonly bool Parity;
        protected WStreamSocket(WStreamSocket baseStream)
        {
            WrappedSocket = baseStream;
            Parity = baseStream.Parity;
        }
        internal WStreamSocket(bool parity)
        {
            // meant for WStreamBaseSocket
            Parity = parity;
        }

        internal void SetSocket(WStreamSocket newSocket)
        {
            WrappedSocket = newSocket;
        }
        /// <summary>
        /// Checks if the websocket is connected
        /// </summary>
        public virtual bool Connected => WrappedSocket.Connected;
        public new virtual void Dispose()
        {
            WrappedSocket.Dispose();
        }
        
        public virtual Task CloseAsync()
        {
            return WrappedSocket.CloseAsync();
        }
        
        /// <summary>
        /// Reads data into the buffer
        /// </summary>
        /// <param name="buffer">destination buffer</param>
        /// <param name="cancellationToken"></param>
        /// <returns>the number of bytes read, 0 if the connection has closed</returns>
        public virtual ValueTask<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            return WrappedSocket.ReadAsync(buffer, cancellationToken);
        }

        /// <summary>
        /// Write the buffer to the websocket
        /// </summary>
        /// <param name="buffer">the source buffer</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public virtual ValueTask WriteAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            return WrappedSocket.WriteAsync(buffer, cancellationToken);
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
            WrappedSocket.CloseAsync().GetAwaiter().GetResult();
        }
        
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            var tsk = ReadAsync(new ArraySegment<byte>(buffer, offset, count), CancellationToken.None);
            return tsk.GetAwaiter().GetResult();
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken).AsTask();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            var tsk = WriteAsync(new ArraySegment<byte>(buffer, offset, count), CancellationToken.None);
            tsk.GetAwaiter().GetResult();
        }

        public override bool CanRead => WrappedSocket.Connected;
        public override bool CanSeek => false;
        public override bool CanWrite => WrappedSocket.Connected;

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