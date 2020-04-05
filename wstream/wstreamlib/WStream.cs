using System;
using System.Collections.Generic;
using System.Threading;
using wstreamlib.Ninja.WebSockets;

namespace wstreamlib
{
    public class WStream
    {
        private readonly WebSocketClientFactory _factory;

        public WStream()
        {
            _factory = new WebSocketClientFactory();
        }

        public WsConnection Connect(Uri uri, CancellationToken cancellationToken, Dictionary<string, string> headers = null)
        {
            var opt = new WebSocketClientOptions();
            if (headers != null)
            {
                opt.AdditionalHttpHeaders = headers;
            }

            var wSock = _factory.ConnectAsync(uri, opt, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
            return new WsConnection(wSock.Item1, wSock.Item2);
        }
    }
}