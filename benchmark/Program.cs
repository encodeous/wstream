using System;
using System.Net;
using System.Threading.Tasks;
using wstream;

int _bufferSize = 1024 * 130;

Console.WriteLine("Starting wstream Benchmark...");
WsServer server = new WsServer();
await server.StartAsync(new IPEndPoint(IPAddress.Any, 12345), async (tunnel) =>
{
    Console.WriteLine($"Server Side Id: {tunnel.ConnectionId}");
    var buffer = new byte[_bufferSize];
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
});
await ClientThread(128 * 1024, 5 * 1073741274L);
await ClientThread(256 * 1024, 5 * 1073741274L);
await ClientThread(350 * 1024, 5 * 1073741274L);
await ClientThread(512 * 1024, 5 * 1073741274L);

Console.WriteLine("Starting endurance test... sending 500gb over network, this will take a while");

await ClientThread(350 * 1024, 500 * 1073741274L);

byte[] buffer;

async Task ClientThread(int bfz, long bytes)
{
    WsClient client = new WsClient();
    _bufferSize = bfz;
    var rng = new Random();
    Console.WriteLine($"\n---- Sending {bytes} bytes with buffer size of {bfz} ----");
    var tunnel = await client.ConnectAsync(new Uri("ws://localhost:12345"));
    Console.WriteLine($"Client Side Id: {tunnel.ConnectionId}");
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
    Console.WriteLine($"Elapsed: {span}\nThroughput: {bytes / span.TotalSeconds:n} Bytes / second\nMessage Rate {messagesSent / span.TotalSeconds:n} Messages / second.");
    await tunnel.DisposeAsync();
}