using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Heijden.DNS;
using wstream;
using wstream.Crypto;

namespace samples
{
    public class TunnelSample
    {
        public async Task Test()
        {
            // create the server
            var server = new WsServer();
            await server.StartAsync(new IPEndPoint(IPAddress.Loopback, 8080), async stream =>
            {
                //await stream.EncryptAsync();
                Console.WriteLine($"Tunnel Opened: remote => localhost:12345");
                var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await sock.ConnectAsync("localhost", 12345);
                var ns = new NetworkStream(sock);
                var t1 = ns.CopyToAsync(stream, 62000);
                var t2 = stream.CopyToAsync(ns, 62000);
                await Task.WhenAny(t1, t2);
                sock.Dispose();
                await stream.DisposeAsync();
                Console.WriteLine($"Tunnel Closed: remote => localhost:25565");
            });

            Socket lSock = new Socket(SocketType.Stream, ProtocolType.Tcp);
            lSock.Bind(new IPEndPoint(IPAddress.Loopback, 1234));
            lSock.Listen(10);
            var cts = new CancellationTokenSource();
            Console.WriteLine($"Started... Listening to localhost:1234. Press ctrl + c to exit");
            Console.CancelKeyPress += (q, e) =>
            {
                cts.Cancel();
            };
            while (!cts.Token.IsCancellationRequested)
            {
                var ksock = await lSock.AcceptAsync();
                Task.Run(async () =>
                {
                    // start client
                    var client = new WsClient();
                    var connection = await client.ConnectAsync(new Uri("ws://localhost:8080"));
                    //await connection.EncryptAsync();
                    var ns = new NetworkStream(ksock);
                    var t1 = ns.CopyToAsync(connection, 62000);
                    var t2 = connection.CopyToAsync(ns, 62000);
                    await Task.WhenAny(t1, t2);
                    ksock.Dispose();
                    await connection.DisposeAsync();
                    client.Dispose();
                });
            }
            // cleanup
            server.Dispose();
        }
    }
}