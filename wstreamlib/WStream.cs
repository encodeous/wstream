using System;
using System.Collections.Generic;
using System.Threading;
using Grpc.Net.Client;

namespace wstreamlib
{
    public class WStream
    {
        public WStream()
        {
        }

        public WsConnection Connect(Uri uri, bool useTls = true)
        {
            if (!useTls)
            {
                AppContext.SetSwitch(
                    "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            }
            var channel = GrpcChannel.ForAddress(uri);
            var client = new WsBinary.WsBinaryClient(channel);
            var stream = client.ExchangeBinary();
            return new WsConnection(stream.ResponseStream, stream.RequestStream, channel);
        }
    }
}