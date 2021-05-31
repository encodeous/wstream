# WStream - WebSocket Streams

[![Download on Nuget](https://img.shields.io/nuget/v/wstream?style=flat-square)](https://www.nuget.org/packages/wstream/)

*WStream offers a simple and high-performance WebSocket stream built on top of Kestrel*

## Use Cases:

- Server-to-Server communication.
- Sending binary data through web proxies like Cloudflare.
- Building custom protocols.
- Writing micro websocket servers without too much boilerplate.
- Bypassing TCP filters through a firewall.

*Wow, that sounds great! How do I use WStream?*

## Usage

WStream offers three main constructs, `WsClient`, `WsServer` and `WsStream`.

Here is a simple example of a ping program built in WStream.

```c#
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
```

## Advanced Usage

WStream also supports extending the existing stream. You are able to overload the underlying websocket with custom behaviour like compression or encryption.

An example usage of this API is shown in the `samples` project.

## Performance

*WStream was built with performance in mind.*

Here is a benchmark performed on Windows 10 Build 19402 with a core i7-6700k and 32gb of system ram.
```
Buffer Size: 65536  Throughput: 558.92 MBps  Elapsed: 00:15:16.0470650  Message Rate: 8,942.77 msg/s  Payload Size: 511,999.00 MB.
```

As you can see, WStream reaches over 4 Gbps through a single connection!

## Current Limitations

Due to the fact that Kestrel is bundled inside of ASP.NET Core, there are a large number of dependencies. There is currently no way around this :(