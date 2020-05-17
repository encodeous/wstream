﻿// ---------------------------------------------------------------------
// Copyright 2018 David Haig
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
// ---------------------------------------------------------------------

using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace wstreamlib.Ninja.WebSockets.Internal
{
    /// <summary>
    /// Main implementation of the WebSocket abstract class
    /// </summary>
    internal class WebSocketImplementation : WebSocket
    {
        internal readonly Guid _guid;
        private readonly Func<MemoryStream> _recycledStreamFactory;
        internal readonly Stream _stream;
        private readonly bool _includeExceptionInCloseResponse;
        private readonly bool _isClient;
        private readonly string _subProtocol;
        private readonly CancellationTokenSource _internalReadCts;
        private WebSocketState _state;
        private bool _isContinuationFrame;
        private WebSocketMessageType _continuationFrameMessageType = WebSocketMessageType.Binary;
        private bool _tryGetBufferFailureLogged = false;
        private const int MaxPingPongPayloadLen = 125;
        private WebSocketCloseStatus? _closeStatus;
        private string _closeStatusDescription;

        internal delegate void ConnectionCloseDelegate();
        internal ConnectionCloseDelegate ConnectionClose;
        internal bool IsClosed;

        public event EventHandler<PongEventArgs> Pong;

        internal WebSocketImplementation(Guid guid, Func<MemoryStream> recycledStreamFactory, Stream stream, TimeSpan keepAliveInterval, bool includeExceptionInCloseResponse, bool isClient, string subProtocol)
        {
            _guid = guid;
            _recycledStreamFactory = recycledStreamFactory;
            _stream = stream;
            _isClient = isClient;
            _subProtocol = subProtocol;
            _internalReadCts = new CancellationTokenSource();
            _state = WebSocketState.Open;

            KeepAliveInterval = keepAliveInterval;
            _includeExceptionInCloseResponse = includeExceptionInCloseResponse;
            if (keepAliveInterval.Ticks < 0)
            {
                throw new InvalidOperationException("KeepAliveInterval must be Zero or positive");
            }

            if (keepAliveInterval == TimeSpan.Zero)
            {
                Events.Log.KeepAliveIntervalZero(guid);
            }
            else
            {
                new PingPongManager(guid, this, keepAliveInterval, _internalReadCts.Token);
            }
        }

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string CloseStatusDescription => _closeStatusDescription;

        public override WebSocketState State { get { return _state; } }

        public override string SubProtocol => _subProtocol;

        public TimeSpan KeepAliveInterval { get; }

        /// <summary>
        /// Receive web socket result
        /// </summary>
        /// <param name="buffer">The buffer to copy data into</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The web socket result details</returns>
        public WebSocketReceiveResult Receive(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            try
            {
                // we may receive control frames so reading needs to happen in an infinite loop
                while (true)
                {
                    // allow this operation to be cancelled from inside OR outside this instance
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_internalReadCts.Token, cancellationToken);
                    WebSocketFrame frame;
                    try
                    {
                        frame = WebSocketFrameReader.Read(_stream, buffer);
                        Events.Log.ReceivedFrame(_guid, frame.OpCode, frame.IsFinBitSet, frame.Count);
                    }
                    catch (InternalBufferOverflowException ex)
                    {
                        CloseOutputAutoTimeout(WebSocketCloseStatus.MessageTooBig, "Frame too large to fit in buffer. Use message fragmentation", ex);
                        throw;
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        CloseOutputAutoTimeout(WebSocketCloseStatus.ProtocolError, "Payload length out of range", ex);
                        throw;
                    }
                    catch (EndOfStreamException ex)
                    {
                        CloseOutputAutoTimeout(WebSocketCloseStatus.InvalidPayloadData, "Unexpected end of stream encountered", ex);
                        throw;
                    }
                    catch (OperationCanceledException ex)
                    {
                        CloseOutputAutoTimeout(WebSocketCloseStatus.EndpointUnavailable, "Operation cancelled", ex);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        CloseOutputAutoTimeout(WebSocketCloseStatus.InternalServerError, "Error reading WebSocket frame", ex);
                        throw;
                    }

                    switch (frame.OpCode)
                    {
                        case WebSocketOpCode.ConnectionClose:
                            return RespondToCloseFrame(frame, linkedCts.Token);
                        case WebSocketOpCode.Ping:
                            ArraySegment<byte> pingPayload = new ArraySegment<byte>(buffer.Array, buffer.Offset, frame.Count);
                            SendPong(pingPayload, linkedCts.Token);
                            break;
                        case WebSocketOpCode.Pong:
                            ArraySegment<byte> pongBuffer = new ArraySegment<byte>(buffer.Array, frame.Count, buffer.Offset);
                            Pong?.Invoke(this, new PongEventArgs(pongBuffer));
                            break;
                        case WebSocketOpCode.TextFrame:
                            if (!frame.IsFinBitSet)
                            {
                                // continuation frames will follow, record the message type Text
                                _continuationFrameMessageType = WebSocketMessageType.Text;
                            }
                            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Text, frame.IsFinBitSet);
                        case WebSocketOpCode.BinaryFrame:
                            if (!frame.IsFinBitSet)
                            {
                                // continuation frames will follow, record the message type Binary
                                _continuationFrameMessageType = WebSocketMessageType.Binary;
                            }
                            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Binary, frame.IsFinBitSet);
                        case WebSocketOpCode.ContinuationFrame:
                            return new WebSocketReceiveResult(frame.Count, _continuationFrameMessageType, frame.IsFinBitSet);
                        default:
                            Exception ex = new NotSupportedException($"Unknown WebSocket opcode {frame.OpCode}");
                            CloseOutputAutoTimeout(WebSocketCloseStatus.ProtocolError, ex.Message, ex);
                            throw ex;
                    }
                }
            }
            catch (Exception catchAll)
            {
                // Most exceptions will be caught closer to their source to send an appropriate close message (and set the WebSocketState)
                // However, if an unhandled exception is encountered and a close message not sent then send one here
                if (_state == WebSocketState.Open)
                {
                    CloseOutputAutoTimeout(WebSocketCloseStatus.InternalServerError, "Unexpected error reading from WebSocket", catchAll);
                }

                throw;
            }
        }

        /// <summary>
        /// Receive web socket result
        /// </summary>
        /// <param name="buffer">The buffer to copy data into</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The web socket result details</returns>
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            try
            {
                // we may receive control frames so reading needs to happen in an infinite loop
                while (true)
                {
                    // allow this operation to be cancelled from iniside OR outside this instance
                    using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_internalReadCts.Token, cancellationToken);
                    WebSocketFrame frame = null;
                    try
                    {
                        frame = await WebSocketFrameReader.ReadAsync(_stream, buffer, linkedCts.Token).ConfigureAwait(false);
                        Events.Log.ReceivedFrame(_guid, frame.OpCode, frame.IsFinBitSet, frame.Count);
                    }
                    catch (InternalBufferOverflowException ex)
                    {
                        await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.MessageTooBig, "Frame too large to fit in buffer. Use message fragmentation", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.ProtocolError, "Payload length out of range", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (EndOfStreamException ex)
                    {
                        await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.InvalidPayloadData, "Unexpected end of stream encountered", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (OperationCanceledException ex)
                    {
                        await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.EndpointUnavailable, "Operation cancelled", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.InternalServerError, "Error reading WebSocket frame", ex).ConfigureAwait(false);
                        throw;
                    }

                    switch (frame.OpCode)
                    {
                        case WebSocketOpCode.ConnectionClose:
                            return await RespondToCloseFrameAsync(frame, linkedCts.Token).ConfigureAwait(false);
                        case WebSocketOpCode.Ping:
                            ArraySegment<byte> pingPayload = new ArraySegment<byte>(buffer.Array, buffer.Offset, frame.Count);
                            await SendPongAsync(pingPayload, linkedCts.Token).ConfigureAwait(false);
                            break;
                        case WebSocketOpCode.Pong:
                            ArraySegment<byte> pongBuffer = new ArraySegment<byte>(buffer.Array, frame.Count, buffer.Offset);
                            Pong?.Invoke(this, new PongEventArgs(pongBuffer));
                            break;
                        case WebSocketOpCode.TextFrame:
                            if (!frame.IsFinBitSet)
                            {
                                // continuation frames will follow, record the message type Text
                                _continuationFrameMessageType = WebSocketMessageType.Text;
                            }
                            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Text, frame.IsFinBitSet);
                        case WebSocketOpCode.BinaryFrame:
                            if (!frame.IsFinBitSet)
                            {
                                // continuation frames will follow, record the message type Binary
                                _continuationFrameMessageType = WebSocketMessageType.Binary;
                            }
                            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Binary, frame.IsFinBitSet);
                        case WebSocketOpCode.ContinuationFrame:
                            return new WebSocketReceiveResult(frame.Count, _continuationFrameMessageType, frame.IsFinBitSet);
                        default:
                            Exception ex = new NotSupportedException($"Unknown WebSocket opcode {frame.OpCode}");
                            await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.ProtocolError, ex.Message, ex).ConfigureAwait(false);
                            throw ex;
                    }
                }
            }
            catch (Exception catchAll)
            {
                // Most exceptions will be caught closer to their source to send an appropriate close message (and set the WebSocketState)
                // However, if an unhandled exception is encountered and a close message not sent then send one here
                if (_state == WebSocketState.Open)
                {
                    await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.InternalServerError, "Unexpected error reading from WebSocket", catchAll).ConfigureAwait(false);
                }

                throw;
            }
        }

        /// <summary>
        /// Send data to the web socket
        /// </summary>
        /// <param name="buffer">the buffer containing data to send</param>
        /// <param name="messageType">The message type. Can be Text or Binary</param>
        /// <param name="endOfMessage">True if this message is a standalone message (this is the norm)
        /// If it is a multi-part message then false (and true for the last message)</param>
        /// <param name="cancellationToken">the cancellation token</param>
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            await using MemoryStream stream = _recycledStreamFactory();
            WebSocketOpCode opCode = GetOppCode(messageType);

            WebSocketFrameWriter.Write(opCode, buffer, stream, endOfMessage, _isClient);
            Events.Log.SendingFrame(_guid, opCode, endOfMessage, buffer.Count, false);

            await WriteStreamToNetwork(stream, cancellationToken).ConfigureAwait(false);
            _isContinuationFrame = !endOfMessage; // TODO: is this correct??
        }

        /// <summary>
        /// Send data to the web socket
        /// </summary>
        /// <param name="buffer">the buffer containing data to send</param>
        /// <param name="messageType">The message type. Can be Text or Binary</param>
        /// <param name="endOfMessage">True if this message is a standalone message (this is the norm)
        /// If it is a multi-part message then false (and true for the last message)</param>
        /// <param name="cancellationToken">the cancellation token</param>
        public void Send(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            using MemoryStream stream = _recycledStreamFactory();
            WebSocketOpCode opCode = GetOppCode(messageType);

            WebSocketFrameWriter.Write(opCode, buffer, stream, endOfMessage, _isClient);
            Events.Log.SendingFrame(_guid, opCode, endOfMessage, buffer.Count, false);

            WriteStreamToNetworkSync(stream, cancellationToken);
            _isContinuationFrame = !endOfMessage; // TODO: is this correct??
        }

        /// <summary>
        /// Call this automatically from server side each keepAliveInterval period
        /// NOTE: ping payload must be 125 bytes or less
        /// </summary>
        public async Task SendPingAsync(ArraySegment<byte> payload, CancellationToken cancellationToken)
        {
            if (payload.Count > MaxPingPongPayloadLen)
            {
                throw new InvalidOperationException($"Cannot send Ping: Max ping message size {MaxPingPongPayloadLen} exceeded: {payload.Count}");
            }

            if (_state == WebSocketState.Open)
            {
                await using MemoryStream stream = _recycledStreamFactory();
                WebSocketFrameWriter.Write(WebSocketOpCode.Ping, payload, stream, true, _isClient);
                Events.Log.SendingFrame(_guid, WebSocketOpCode.Ping, true, payload.Count, false);
                await WriteStreamToNetwork(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Aborts the WebSocket without sending a Close frame
        /// </summary>
        public override void Abort()
        {
            _state = WebSocketState.Aborted;
            _internalReadCts.Cancel();
            if (!IsClosed)
            {
                IsClosed = true;
                ConnectionClose?.Invoke();
            }
        }

        /// <summary>
        /// Polite close (use the close handshake)
        /// </summary>
        public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            if (_state == WebSocketState.Open)
            {
                await using MemoryStream stream = _recycledStreamFactory();
                ArraySegment<byte> buffer = BuildClosePayload(closeStatus, statusDescription);
                WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, buffer, stream, true, _isClient);
                Events.Log.CloseHandshakeStarted(_guid, closeStatus, statusDescription);
                Events.Log.SendingFrame(_guid, WebSocketOpCode.ConnectionClose, true, buffer.Count, true);
                await WriteStreamToNetwork(stream, cancellationToken).ConfigureAwait(false);
                _state = WebSocketState.CloseSent;
            }
            else
            {
                Events.Log.InvalidStateBeforeClose(_guid, _state);
            }
        }

        /// <summary>
        /// Fire and forget close
        /// </summary>
        public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            if (_state == WebSocketState.Open)
            {
                _state = WebSocketState.Closed; // set this before we write to the network because the write may fail

                await using MemoryStream stream = _recycledStreamFactory();
                ArraySegment<byte> buffer = BuildClosePayload(closeStatus, statusDescription);
                WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, buffer, stream, true, _isClient);
                Events.Log.CloseOutputNoHandshake(_guid, closeStatus, statusDescription);
                Events.Log.SendingFrame(_guid, WebSocketOpCode.ConnectionClose, true, buffer.Count, true);
                await WriteStreamToNetwork(stream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Events.Log.InvalidStateBeforeCloseOutput(_guid, _state);
            }

            // cancel pending reads
            _internalReadCts.Cancel();
        }

        /// <summary>
        /// Fire and forget close
        /// </summary>
        public void CloseOutput(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            if (_state == WebSocketState.Open)
            {
                _state = WebSocketState.Closed; // set this before we write to the network because the write may fail

                using (MemoryStream stream = _recycledStreamFactory())
                {
                    ArraySegment<byte> buffer = BuildClosePayload(closeStatus, statusDescription);
                    WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, buffer, stream, true, _isClient);
                    Events.Log.CloseOutputNoHandshake(_guid, closeStatus, statusDescription);
                    Events.Log.SendingFrame(_guid, WebSocketOpCode.ConnectionClose, true, buffer.Count, true);
                    WriteStreamToNetworkSync(stream, cancellationToken);
                }
                if (!IsClosed)
                {
                    IsClosed = true;
                    ConnectionClose?.Invoke();
                }
            }
            else
            {
                Events.Log.InvalidStateBeforeCloseOutput(_guid, _state);
            }

            // cancel pending reads
            _internalReadCts.Cancel();
        }

        /// <summary>
        /// Dispose will send a close frame if the connection is still open
        /// </summary>
        public override void Dispose()
        {
            Events.Log.WebSocketDispose(_guid, _state);

            try
            {
                if (_state == WebSocketState.Open)
                {
                    CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Service is Disposed", cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        // log don't throw
                        Events.Log.WebSocketDisposeCloseTimeout(_guid, _state);
                    }
                }

                // cancel pending reads - usually does nothing
                _internalReadCts.Cancel();
                _stream.Close();
            }
            catch (Exception ex)
            {
                // log dont throw
                Events.Log.WebSocketDisposeError(_guid, _state, ex.ToString());
            }
        }

        /// <summary>
        /// Called when a Pong frame is received
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnPong(PongEventArgs e)
        {
            Pong?.Invoke(this, e);
        }

        /// <summary>
        /// As per the spec, write the close status followed by the close reason
        /// </summary>
        /// <param name="closeStatus">The close status</param>
        /// <param name="statusDescription">Optional extra close details</param>
        /// <returns>The payload to sent in the close frame</returns>
        private ArraySegment<byte> BuildClosePayload(WebSocketCloseStatus closeStatus, string statusDescription)
        {
            byte[] statusBuffer = BitConverter.GetBytes((ushort)closeStatus);
            Array.Reverse(statusBuffer); // network byte order (big endian)

            if (statusDescription == null)
            {
                return new ArraySegment<byte>(statusBuffer);
            }
            else
            {
                byte[] descBuffer = Encoding.UTF8.GetBytes(statusDescription);
                byte[] payload = new byte[statusBuffer.Length + descBuffer.Length];
                Buffer.BlockCopy(statusBuffer, 0, payload, 0, statusBuffer.Length);
                Buffer.BlockCopy(descBuffer, 0, payload, statusBuffer.Length, descBuffer.Length);
                return new ArraySegment<byte>(payload);
            }
        }

        private async Task SendPongAsync(ArraySegment<byte> payload, CancellationToken cancellationToken)
        {
            // NOTE: pong payload must be 125 bytes or less
            // Pong should contain the same payload as the ping
            // as per websocket spec
            if (payload.Count > MaxPingPongPayloadLen)
            {
                Exception ex = new InvalidOperationException($"Max ping message size {MaxPingPongPayloadLen} exceeded: {payload.Count}");
                await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.ProtocolError, ex.Message, ex).ConfigureAwait(false);
                throw ex;
            }

            try
            {
                if (_state == WebSocketState.Open)
                {
                    await using MemoryStream stream = _recycledStreamFactory();
                    WebSocketFrameWriter.Write(WebSocketOpCode.Pong, payload, stream, true, _isClient);
                    Events.Log.SendingFrame(_guid, WebSocketOpCode.Pong, true, payload.Count, false);
                    await WriteStreamToNetwork(stream, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.EndpointUnavailable, "Unable to send Pong response", ex).ConfigureAwait(false);
                throw;
            }
        }
        private void SendPong(ArraySegment<byte> payload, CancellationToken cancellationToken)
        {
            // as per websocket spec
            if (payload.Count > MaxPingPongPayloadLen)
            {
                Exception ex = new InvalidOperationException($"Max ping message size {MaxPingPongPayloadLen} exceeded: {payload.Count}");
                CloseOutputAutoTimeout(WebSocketCloseStatus.ProtocolError, ex.Message, ex);
                throw ex;
            }

            try
            {
                if (_state == WebSocketState.Open)
                {
                    using MemoryStream stream = _recycledStreamFactory();
                    WebSocketFrameWriter.Write(WebSocketOpCode.Pong, payload, stream, true, _isClient);
                    Events.Log.SendingFrame(_guid, WebSocketOpCode.Pong, true, payload.Count, false);
                    WriteStreamToNetworkSync(stream, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                CloseOutputAutoTimeout(WebSocketCloseStatus.EndpointUnavailable, "Unable to send Pong response", ex);
                throw;
            }
        }

        /// <summary>
        /// Called when a Close frame is received
        /// Send a response close frame if applicable
        /// </summary>
        private async Task<WebSocketReceiveResult> RespondToCloseFrameAsync(WebSocketFrame frame, CancellationToken token)
        {
            _closeStatus = frame.CloseStatus;
            _closeStatusDescription = frame.CloseStatusDescription;

            if (_state == WebSocketState.CloseSent)
            {
                // this is a response to close handshake initiated by this instance
                _state = WebSocketState.Closed;
                Events.Log.CloseHandshakeComplete(_guid);
            }
            else if (_state == WebSocketState.Open)
            {
                // do not echo the close payload back to the client, there is no requirement for it in the spec. 
                // However, the same CloseStatus as received should be sent back.
                ArraySegment<byte> closePayload = new ArraySegment<byte>(Array.Empty<byte>(), 0, 0);
                _state = WebSocketState.CloseReceived;
                Events.Log.CloseHandshakeRespond(_guid, frame.CloseStatus, frame.CloseStatusDescription);

                await using MemoryStream stream = _recycledStreamFactory();
                WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, closePayload, stream, true, _isClient);
                Events.Log.SendingFrame(_guid, WebSocketOpCode.ConnectionClose, true, closePayload.Count, false);
                await WriteStreamToNetwork(stream, token).ConfigureAwait(false);
            }
            else
            {
                Events.Log.CloseFrameReceivedInUnexpectedState(_guid, _state, frame.CloseStatus, frame.CloseStatusDescription);
            }

            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Close, frame.IsFinBitSet, frame.CloseStatus, frame.CloseStatusDescription);
        }

        /// <summary>
        /// Called when a Close frame is received
        /// Send a response close frame if applicable
        /// </summary>
        private WebSocketReceiveResult RespondToCloseFrame(WebSocketFrame frame, CancellationToken token)
        {
            _closeStatus = frame.CloseStatus;
            _closeStatusDescription = frame.CloseStatusDescription;

            if (_state == WebSocketState.CloseSent)
            {
                // this is a response to close handshake initiated by this instance
                _state = WebSocketState.Closed;
                Events.Log.CloseHandshakeComplete(_guid);
            }
            else if (_state == WebSocketState.Open)
            {
                // do not echo the close payload back to the client, there is no requirement for it in the spec. 
                // However, the same CloseStatus as recieved should be sent back.
                ArraySegment<byte> closePayload = new ArraySegment<byte>(Array.Empty<byte>(), 0, 0);
                _state = WebSocketState.CloseReceived;
                Events.Log.CloseHandshakeRespond(_guid, frame.CloseStatus, frame.CloseStatusDescription);

                using MemoryStream stream = _recycledStreamFactory();
                WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, closePayload, stream, true, _isClient);
                Events.Log.SendingFrame(_guid, WebSocketOpCode.ConnectionClose, true, closePayload.Count, false);
                WriteStreamToNetworkSync(stream, token);
            }
            else
            {
                Events.Log.CloseFrameReceivedInUnexpectedState(_guid, _state, frame.CloseStatus, frame.CloseStatusDescription);
            }
            if (!IsClosed)
            {
                IsClosed = true;
                ConnectionClose?.Invoke();
            }
            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Close, frame.IsFinBitSet, frame.CloseStatus, frame.CloseStatusDescription);
        }

        /// <summary>
        /// Note that the way in which the stream buffer is accessed can lead to significant performance problems
        /// You want to avoid a call to stream.ToArray to avoid extra memory allocation
        /// MemoryStream can be configured to have its internal buffer accessible.
        /// </summary>
        private ArraySegment<byte> GetBuffer(MemoryStream stream)
        {
            // Avoid calling ToArray on the MemoryStream because it allocates a new byte array on tha heap
            // We avaoid this by attempting to access the internal memory stream buffer
            // This works with supported streams like the recyclable memory stream and writable memory streams
            if (!stream.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                if (!_tryGetBufferFailureLogged)
                {
                    Events.Log.TryGetBufferNotSupported(_guid, stream?.GetType()?.ToString());
                    _tryGetBufferFailureLogged = true;
                }

                // internal buffer not suppoted, fall back to ToArray()
                byte[] array = stream.ToArray();
                buffer = new ArraySegment<byte>(array, 0, array.Length);
            }

            return new ArraySegment<byte>(buffer.Array, buffer.Offset, (int)stream.Position);
        }

        private volatile bool _writing;
        private SpinWait spinner = new SpinWait();

        /// <summary>
        /// Puts data on the wire
        /// </summary>
        /// <param name="stream">The stream to read data from</param>
        private async Task WriteStreamToNetwork(MemoryStream stream, CancellationToken cancellationToken)
        {
            ArraySegment<byte> buffer = GetBuffer(stream);
            while (_writing && !cancellationToken.IsCancellationRequested)
            {
                spinner.SpinOnce();
            }
            if (!_writing)
            {
                _writing = true;
                await _stream.WriteAsync(buffer.Array, buffer.Offset, buffer.Count, cancellationToken).ConfigureAwait(false);
                _writing = false;
            }
        }

        /// <summary>
        /// Puts data on the wire
        /// </summary>
        /// <param name="stream">The stream to read data from</param>
        private void WriteStreamToNetworkSync(MemoryStream stream, CancellationToken cancellationToken)
        {
            ArraySegment<byte> buffer = GetBuffer(stream);
            // ReSharper disable once AsyncConverter.AsyncWait
            while (_writing && !cancellationToken.IsCancellationRequested)
            {
                spinner.SpinOnce();
            }
            if (!_writing)
            {
                _writing = true;
                _stream.Write(buffer.Array, buffer.Offset, buffer.Count);
                _writing = false;
            }
        }

        /// <summary>
        /// Turns a spec websocket frame opcode into a WebSocketMessageType
        /// </summary>
        private WebSocketOpCode GetOppCode(WebSocketMessageType messageType)
        {
            if (_isContinuationFrame)
            {
                return WebSocketOpCode.ContinuationFrame;
            }
            else
            {
                return messageType switch
                {
                    WebSocketMessageType.Binary => WebSocketOpCode.BinaryFrame,
                    WebSocketMessageType.Text => WebSocketOpCode.TextFrame,
                    WebSocketMessageType.Close => throw new NotSupportedException(
                        "Cannot use Send function to send a close frame. Use Close function."),
                    _ => throw new NotSupportedException($"MessageType {messageType} not supported")
                };
            }
        }

        /// <summary>
        /// Automatic WebSocket close in response to some invalid data from the remote websocket host
        /// </summary>
        /// <param name="closeStatus">The close status to use</param>
        /// <param name="statusDescription">A description of why we are closing</param>
        /// <param name="ex">The exception (for logging)</param>
        private void CloseOutputAutoTimeout(WebSocketCloseStatus closeStatus, string statusDescription, Exception ex)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(5);
            Events.Log.CloseOutputAutoTimeout(_guid, closeStatus, statusDescription, ex.ToString());

            try
            {
                // we may not want to send sensitive information to the client / server
                if (_includeExceptionInCloseResponse)
                {
                    statusDescription = statusDescription + "\r\n\r\n" + ex.ToString();
                }

                var autoCancel = new CancellationTokenSource(timeSpan);
                CloseOutput(closeStatus, statusDescription, autoCancel.Token);
                if (!IsClosed)
                {
                    IsClosed = true;
                    ConnectionClose?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                // do not throw an exception because that will mask the original exception
                Events.Log.CloseOutputAutoTimeoutCancelled(_guid, (int)timeSpan.TotalSeconds, closeStatus, statusDescription, ex.ToString());
            }
            catch (Exception closeException)
            {
                // do not throw an exception because that will mask the original exception
                Events.Log.CloseOutputAutoTimeoutError(_guid, closeException.ToString(), closeStatus, statusDescription, ex.ToString());
            }
        }

        /// <summary>
        /// Automatic WebSocket close in response to some invalid data from the remote websocket host
        /// </summary>
        /// <param name="closeStatus">The close status to use</param>
        /// <param name="statusDescription">A description of why we are closing</param>
        /// <param name="ex">The exception (for logging)</param>
        private async Task CloseOutputAutoTimeoutAsync(WebSocketCloseStatus closeStatus, string statusDescription, Exception ex)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(5);
            Events.Log.CloseOutputAutoTimeout(_guid, closeStatus, statusDescription, ex.ToString());

            try
            {
                // we may not want to send sensitive information to the client / server
                if (_includeExceptionInCloseResponse)
                {
                    statusDescription = statusDescription + "\r\n\r\n" + ex.ToString();
                }

                var autoCancel = new CancellationTokenSource(timeSpan);
                await CloseOutputAsync(closeStatus, statusDescription, autoCancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // do not throw an exception because that will mask the original exception
                Events.Log.CloseOutputAutoTimeoutCancelled(_guid, (int)timeSpan.TotalSeconds, closeStatus, statusDescription, ex.ToString());
            }
            catch (Exception closeException)
            {
                // do not throw an exception because that will mask the original exception
                Events.Log.CloseOutputAutoTimeoutError(_guid, closeException.ToString(), closeStatus, statusDescription, ex.ToString());
            }
        }
    }
}