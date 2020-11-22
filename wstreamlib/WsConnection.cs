using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
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

        private GrpcChannel _channel = null;

        private AsyncManualResetEvent _mre = null;
        private IAsyncStreamReader<Binary> _readStream;
        private IAsyncStreamWriter<Binary> _writeStream;

        public WsConnection(IAsyncStreamReader<Binary> requestStream,
            IAsyncStreamWriter<Binary> responseStream, AsyncManualResetEvent disconnectEvent)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _sCancellationToken = _cancellationTokenSource.Token;
            _readStream = requestStream;
            _writeStream = responseStream;
            _mre = disconnectEvent;
            Connected = true;
        }
        public WsConnection(IAsyncStreamReader<Binary> requestStream,
            IAsyncStreamWriter<Binary> responseStream, GrpcChannel channel)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _sCancellationToken = _cancellationTokenSource.Token;
            _readStream = requestStream;
            _writeStream = responseStream;
            _channel = channel;
            Connected = true;
        }

        public void Close()
        {
            _cancellationTokenSource.Cancel();
            Connected = false;
            ConnectionClosedEvent?.Invoke(this);
            _mre?.Set();
            _channel?.ShutdownAsync();
        }

        public async Task Write(ArraySegment<byte> arr)
        {
            await _writeStream.WriteAsync(new Binary(){Data = ByteString.CopyFrom(arr)});
        }

        private byte[] tempCache = new byte[1000000];
        private int cacheLen = 0;
        private int cacheStart = 0;
        public async Task<int> Read(ArraySegment<byte> arr)
        {
            if (arr.Count == 0) return 0;
            if (cacheLen >= arr.Count)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    arr[i] = tempCache[i + cacheStart];
                }

                cacheStart += arr.Count;
                cacheLen -= arr.Count;
                return arr.Count;
            }
            else if (cacheLen == 0)
            {
                cacheStart = 0;
                bool connected = await _readStream.MoveNext(_sCancellationToken);
                if (!connected)
                {
                    Close();
                    return 0;
                }
                var dat = _readStream.Current.Data;
                if (dat.Length > arr.Count)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        arr[i] = dat[i];
                    }

                    cacheLen = dat.Length - arr.Count;
                    for (int i = 0; i < cacheLen; i++)
                    {
                        tempCache[i] = dat[i + arr.Count];
                    }

                    return arr.Count;
                }
                else
                {
                    for (int i = 0; i < dat.Length; i++)
                    {
                        arr[i] = dat[i];
                    }

                    return dat.Length;
                }
            }
            else
            {
                for (int i = 0; i < cacheLen; i++)
                {
                    arr[i] = tempCache[i + cacheStart];
                }

                int tlen = cacheLen;
                cacheLen = 0;
                return tlen;
            }
        }
    }
}
