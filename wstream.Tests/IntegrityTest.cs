using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;
using Xunit;

namespace wstream.Tests
{
    public class IntegrityTest : IClassFixture<IntegrityTestFixture>
    {
        private IntegrityTestFixture _fixture;

        public IntegrityTest(IntegrityTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [MemberData(nameof(GenerateIntegrityData))]
        public async Task ValidateIntegrity(int send)
        {
            await _fixture.Writer.WriteAsync(send);
            Assert.Equal(send, await _fixture.Reader.ReadInt32Async());
        }

        public static IEnumerable<object[]> GenerateIntegrityData()
        {
            var rand = new Random();
            for (int i = 0; i < 100; i++)
            {
                yield return new object[] {rand.Next(0, 1000000000)};
            }
        }
    }
    public class IntegrityTestFixture : IDisposable
    {
        public WsStream Client, Server;
        public AsyncBinaryReader Reader;
        public AsyncBinaryWriter Writer;
        public IntegrityTestFixture()
        {
            // create the server
            var server = new WsServer();
            server.StartAsync(new IPEndPoint(IPAddress.Loopback, 8080), async stream =>
            {
                Server = stream;
            }).GetAwaiter().GetResult();

            // start client
            var client = new WsClient();
            Client = client.ConnectAsync(new Uri("ws://localhost:8080")).GetAwaiter().GetResult();
            Thread.Sleep(1000);
            Reader = new AsyncBinaryReader(Client);
            Writer = new AsyncBinaryWriter(Server);
            
        }

        public void Dispose()
        {
            Client?.Dispose();
            Server?.Dispose();
        }
    }
}