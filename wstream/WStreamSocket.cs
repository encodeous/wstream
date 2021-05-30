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

        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> arraySegment))
            {
                WebSocketReceiveResult r = await ReceiveAsync(arraySegment, cancellationToken).ConfigureAwait(false);
                return new ValueWebSocketReceiveResult(r.Count, r.MessageType, r.EndOfMessage);
            }

            byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                WebSocketReceiveResult r = await ReceiveAsync(new ArraySegment<byte>(array, 0, buffer.Length), cancellationToken).ConfigureAwait(false);
                new Span<byte>(array, 0, r.Count).CopyTo(buffer.Span);
                return new ValueWebSocketReceiveResult(r.Count, r.MessageType, r.EndOfMessage);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        public virtual ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            new ValueTask(MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> arraySegment) ?
                SendAsync(arraySegment, messageType, endOfMessage, cancellationToken) :
                SendWithArrayPoolAsync(buffer, messageType, endOfMessage, cancellationToken));
        
        private async Task SendWithArrayPoolAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.Span.CopyTo(array);
                await SendAsync(new ArraySegment<byte>(array, 0, buffer.Length), messageType, endOfMessage, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }
    }
}