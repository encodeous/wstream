﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Nito.AsyncEx.Synchronous;

namespace wstreamlib
{
    public class WsConnection
    {
        public readonly Guid ConnectionId = Guid.NewGuid();
        public bool Connected { get; private set; }
        public delegate void ConnectionCloseDelegate(WsConnection connection);
        public event ConnectionCloseDelegate ConnectionClosedEvent;

        private CancellationToken _sCancellationToken;
        private CancellationTokenSource _cancellationTokenSource;

        private WebSocket _socket;

        public WsConnection(WebSocket webSocket)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _sCancellationToken = _cancellationTokenSource.Token;
            _socket = webSocket;
            Connected = true;
        }

        public Task Close()
        {
            _cancellationTokenSource.Cancel();
            Connected = false;
            ConnectionClosedEvent?.Invoke(this);
            return _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }

        public Task Write(ArraySegment<byte> arr)
        {
            return _socket.SendAsync(arr, WebSocketMessageType.Binary, true, _sCancellationToken);
        }

        public async Task<int> Read(ArraySegment<byte> arr)
        {
            var res = await _socket.ReceiveAsync(arr, _sCancellationToken);
            if (res.CloseStatus != null)
            {
                await Close();
                return 0;
            }

            return res.Count;
        }


    }
}
