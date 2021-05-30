using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using wstream;

namespace samples
{
    public class PingTestSimple
    {
        public async Task Test()
        {
            // create the server
            var server = new WsServer();
            await server.StartAsync(new IPEndPoint(IPAddress.Any, 8080), async stream =>
            {
                // called when a client connects
                await stream.WriteAsync(BitConverter.GetBytes(DateTime.Now.ToBinary()));
                await stream.CloseAsync();
            });

            // start client
            var client = new WsClient();
            var connection = await client.ConnectAsync(new Uri("ws://localhost:8080"));
            // read data
            var binReader = new BinaryReader(connection);
            Console.WriteLine($"Current time is {DateTime.FromBinary(binReader.ReadInt64())}");

            // cleanup
            client.Dispose();
            server.Dispose();
        }
    }
}