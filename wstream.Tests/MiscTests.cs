using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;
using Xunit;

namespace wstream.Tests
{
    class InterceptData
    {
        public long BytesWritten;
        public long BytesRead;
    }
    /// <summary>
    /// Measures network in / out
    /// </summary>
    class Interceptor : WStreamSocket
    {
        private InterceptData _data;

        public Interceptor(WStreamSocket socket, out InterceptData data) : base(socket)
        {
            _data = new InterceptData();
            data = _data;
        }
        public override Task CloseAsync()
        {
            return WrappedSocket.CloseAsync();
        }

        public override async ValueTask<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            int len = await WrappedSocket.ReadAsync(buffer, cancellationToken);
            _data.BytesRead += len;
            return len;
        }

        public override ValueTask WriteAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            _data.BytesWritten += buffer.Count;
            return WrappedSocket.WriteAsync(buffer, cancellationToken);
        }
    }
    public class MiscTests
    {
        [Fact]
        public async Task ValidateReadWriteStatistics()
        {
            InterceptData clientData = null, serverData = null;
            var server = new WsServer();
            await server.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), async stream =>
            {
                await stream.WrapSocketAsync(x =>
                    Task.FromResult<WStreamSocket>(new Interceptor(x, out serverData))
                );
                var abr = new AsyncBinaryReader(stream);
                for (int i = 0; i < 100000; i++)
                {
                    Assert.Equal(i, await abr.ReadInt32Async());
                }
                await stream.CloseAsync();
            });
            
            var client = new WsClient();
            var connection = await client.ConnectAsync(new Uri("ws://"+server.ListeningAddresses[0].Substring(7)));
            await connection.WrapSocketAsync(x =>
                Task.FromResult<WStreamSocket>(new Interceptor(x, out clientData))
            );
            var abw = new AsyncBinaryWriter(connection);
            for (int i = 0; i < 100000; i++)
            {
                await abw.WriteAsync(i);
            }

            await abw.FlushAsync();
            
            await Task.Delay(1000);
            
            Assert.Equal(4 * 100000, clientData.BytesWritten);
            Assert.Equal(4 * 100000, serverData.BytesRead);
            
            client.Dispose();
            server.Dispose();
        }
    }
}