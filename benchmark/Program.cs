using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using wstream;

int _bufferSize = 1024 * 130;

WsServer server = new WsServer();

Console.WriteLine("Starting wstream Benchmark...");

await ClientThread(4 * 1024,   (long)(0.7 * 1073741274L));
await ClientThread(16 * 1024,  (3 * 1073741274L));
await ClientThread(32 * 1024,  (3 * 1073741274L));
await ClientThread(60000,  (3 * 1073741274L));
await ClientThread(64 * 1024,  (3 * 1073741274L));
await ClientThread(128 * 1024, (3 * 1073741274L));
await ClientThread(256 * 1024, (3 * 1073741274L));
// // 4k - 10k
// for (int i = 4; i <= 10; i++)
// {
//     await ClientThread(i * 1024, (long)(0.7 * 1073741274L));
// }
// // 15k - 100k
// for (int i = 15; i <= 100; i += 5)
// {
//     await ClientThread(i * 1024, (5 * 1073741274L));
// }
// // 200k - 1m
// for (int i = 200; i <= 1001; i += 100)
// {
//     await ClientThread(i * 1024, (5 * 1073741274L));
// }

Console.WriteLine("\n\nStarting endurance test... sending 500gb over network, this will take a while");

await ClientThread(65536, 500 * 1073741274L);

byte[] buffer;

async Task ClientThread(int bfz, long bytes)
{
    await server.StartAsync(new IPEndPoint(IPAddress.Loopback, 12345), async (tunnel) =>
    {
        var buffer = new byte[bfz];
        long bytesRead = 0;
        try
        {
            while (tunnel.Connected)
            {
                int len = await tunnel.ReadAsync(buffer);
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
    await Task.Delay(500);
    var tunnel = await client.ConnectAsync(new Uri($"ws://{IPAddress.Loopback}:12345"));
    long byteSent = 0;
    long messagesSent = 0;
    buffer = new byte[_bufferSize];
    rng.NextBytes(buffer);

    DateTime timeNow = DateTime.Now;
    while (byteSent < bytes && tunnel.CanWrite)
    {
        await tunnel.WriteAsync(buffer, 0, _bufferSize);
        byteSent += _bufferSize;
        messagesSent++;
    }
    DateTime timeEnd = DateTime.Now;

    TimeSpan span = timeEnd - timeNow;
    Console.WriteLine($"Buffer Size: {bfz}".PadLeft(30) 
                      + $"Throughput: {(bytes / span.TotalSeconds / 1048576):n} MBps".PadLeft(30)
                      + $"Elapsed: {span}".PadLeft(30)
                      + $"Message Rate: {messagesSent / span.TotalSeconds:n} msg/s".PadLeft(40)
                      + $"Payload Size: {bytes / 1048576:n} MB.".PadLeft(40)
                      );
    await tunnel.DisposeAsync();
    await server.StopAsync();
    await Task.Delay(500);
}