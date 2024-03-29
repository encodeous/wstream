﻿using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using wstream;

namespace samples
{
    class InterceptData
    {
        public long BytesWritten;
        public long BytesRead;
    }
    /// <summary>
    /// This is just a example of tracking network in / out
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
            Console.WriteLine("Socket Closed");
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
    public class WsWrapTest
    {
        public async Task Test()
        {
            InterceptData clientData = null, serverData = null;
            // create the server
            var server = new WsServer();
            await server.StartAsync(new IPEndPoint(IPAddress.Any, 8080), async stream =>
            {
                // measure server data
                await stream.WrapSocketAsync(x =>
                    // create a new interceptor
                    Task.FromResult<WStreamSocket>(new Interceptor(x, out serverData))
                );
                // called when a client connects
                await stream.WriteAsync(BitConverter.GetBytes(DateTime.Now.ToBinary()));
                await stream.CloseAsync();
            });

            // start client
            var client = new WsClient();
            var connection = await client.ConnectAsync(new Uri("ws://localhost:8080"));
            // measure client data
            await connection.WrapSocketAsync(x => 
                // create a new interceptor
                Task.FromResult<WStreamSocket>(new Interceptor(x, out clientData))
                );
            // read data
            var binReader = new BinaryReader(connection);
            Console.WriteLine($"Current time is {DateTime.FromBinary(binReader.ReadInt64())}");

            Console.WriteLine($"Intercepted Data from client - Written {clientData.BytesWritten} / Read {clientData.BytesRead}");
            Console.WriteLine($"Intercepted Data from server - Written {serverData.BytesWritten} / Read {serverData.BytesRead}");
            
            // cleanup
            client.Dispose();
            server.Dispose();
        }
    }
}