using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;
using Nito.AsyncEx;

namespace wstreamlib
{
    class WsBinaryServerService : WsBinary.WsBinaryBase
    {
        private WStreamServer _server;

        public WsBinaryServerService(WStreamServer server)
        {
            _server = server;
        }
        public override async Task ExchangeBinary(IAsyncStreamReader<Binary> requestStream, 
            IServerStreamWriter<Binary> responseStream, ServerCallContext context)
        {
            var mre = new AsyncManualResetEvent();
            _server.AddConnection(new WsConnection(requestStream, responseStream, mre));
            await mre.WaitAsync();
        }
    }
}
