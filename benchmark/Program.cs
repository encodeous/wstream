using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using wstream;

class Program
{
    static int _bufferSize = 1024 * 130;

    static WsServer server = new WsServer();
    static byte[] buffer;
    private const long transferBytes = 5 * 1000000000L;

    static void Main(string[] args)
    {
        Console.WriteLine("Starting wstream Benchmark...");
        Console.WriteLine($"| .NET Version | Buffer Size | Packets Per Second | Throughput |");
        Console.WriteLine($"| ------------ | ----------- | ------------------ | ---------- |");
        ClientThread(8 * 1024, transferBytes).GetAwaiter().GetResult();
        ClientThread(64 * 1024, transferBytes).GetAwaiter().GetResult();
        ClientThread(128 * 1024, transferBytes).GetAwaiter().GetResult();
        ClientThread(256 * 1024, transferBytes).GetAwaiter().GetResult();
    }

    static async Task ClientThread(int bfz, long bytes)
    {
        await server.StartAsync(new IPEndPoint(IPAddress.Loopback, 12345), async (tunnel) =>
        {
            var buffer = new byte[bfz];
            long bytesRead = 0;
            try
            {
                while (tunnel.Connected)
                {
                    int len = await tunnel.ReadAsync(new ArraySegment<byte>(buffer));
                    bytesRead += len;
                }
            }
            catch
            {
                await tunnel.CloseAsync();
            }
        }, bfz);
        WsClient client = new WsClient();
        _bufferSize = bfz;
        var rng = new Random();
        await Task.Delay(1000);
        var tunnel2 = await client.ConnectAsync(new Uri($"ws://{IPAddress.Loopback}:12345"));
        long byteSent = 0;
        long messagesSent = 0;
        buffer = new byte[_bufferSize];
        rng.NextBytes(buffer);

        DateTime timeNow = DateTime.Now;
        while (byteSent < bytes && tunnel2.CanWrite)
        {
            await tunnel2.WriteAsync(buffer, 0, _bufferSize);
            byteSent += _bufferSize;
            messagesSent++;
        }

        DateTime timeEnd = DateTime.Now;

        TimeSpan span = timeEnd - timeNow;
        Console.WriteLine($"| {RuntimeInformation.FrameworkDescription} | {bfz:N0} | {messagesSent / span.TotalSeconds:n} msg/s | {(bytes / span.TotalSeconds / 1000000):n} MB/s |");
        tunnel2.Dispose();
        await server.StopAsync();
        await Task.Delay(500);
    }
}