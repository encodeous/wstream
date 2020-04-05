using System;
using System.Net;
using System.Threading;
using wstreamlib;

namespace wstreambenchmark
{
    class Program
    {
        private static Random rng = new Random();
        static void Main(string[] args)
        {
            Console.WriteLine("Starting wstream Benchmark...");
            WStreamServer server = new WStreamServer();
            server.Listen(new IPEndPoint(IPAddress.Any, 12345));
            WStream wsClient = new WStream();
            new Thread(() => ServerThread(server)).Start();
            new Thread(() => ClientThread(wsClient)).Start();
            Thread.Sleep(-1);
        }

        private static int _bufferSize = 1024;

        private static void ServerThread(WStreamServer server)
        {
            while (true)
            {
                var tunnel = server.AcceptConnectionAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[_bufferSize]);
                try
                {
                    while (tunnel.Connected)
                    {
                        if (buffer.Array.Length != _bufferSize)
                        {
                            buffer = new ArraySegment<byte>(new byte[_bufferSize]);
                        }
                        int len = tunnel.Read(buffer);
                        tunnel.Write(buffer.Slice(0, len));
                    }
                }
                catch
                {
                    tunnel.Close();
                }
            }
        }

        private static void ClientThread(WStream client)
        {
            Console.WriteLine("Sending 10gb of data with various packet sizes and waiting for response.");
            for (int i = 14; i < 25; i++)
            {
                _bufferSize = (int)Math.Pow(2, i);
                var tunnel = client.Connect(new Uri("http://localhost:12345"),CancellationToken.None);
                long bytes = (long)Math.Pow(2,30);
                long byteSent = 0;
                long byteReceived = 0;
                long messagesSent = 0;
                DateTime timeNow = DateTime.Now;
                ArraySegment<byte> sbuf = new ArraySegment<byte>(new byte[_bufferSize]);
                while ((byteSent < bytes || byteReceived < bytes) && tunnel.Connected)
                {
                    if (byteSent < bytes)
                    {
                        Send(_bufferSize, tunnel);
                        byteSent += _bufferSize;
                        messagesSent++;
                    }

                    if (byteReceived < bytes)
                    {
                        int len = tunnel.Read(sbuf);
                        byteReceived += len;
                    }
                }
                DateTime timeEnd = DateTime.Now;
                TimeSpan span = timeEnd - timeNow;
                Console.WriteLine($"{_bufferSize} Elapsed {span}, {bytes/span.TotalSeconds} bytes / second, {messagesSent/span.TotalSeconds} messages / second.");
                tunnel.Close();
                client = new WStream();
            }
        }

        private static byte[] buffer;

        private static void Send(int size, WsConnection connection)
        {
            if (buffer == null || buffer.Length != size)
            {
                buffer = new byte[size];
                rng.NextBytes(buffer);
            }
            connection.Write(buffer);
        }
    }
}
