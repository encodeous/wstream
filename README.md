# WStream - WebSocket Streams

[![Download on Nuget](https://img.shields.io/nuget/v/wstream?style=flat-square)](https://www.nuget.org/packages/wstream/)

WStream offers a simple and high-performance WebSocket stream built on top of Kestrel. It targets .NET Standard 2.0, as well as .NET Core 3.1 and above.

## Use Cases:

- Server-to-Server communication.
- Sending binary data through web proxies like Cloudflare.
- Building custom protocols.
- Writing micro websocket servers without boilerplate.

*Wow, that sounds great! How do I use WStream?*

## Usage

WStream offers three main constructs, `WsClient`, `WsServer` and `WsStream`.

### Note
For versions of .NET Core 3.1 and above, the ASP.NET Core runtime has to be installed.

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

## Performance

All benchmarks are run on a Windows 10 machine with an Intel i7-6700K CPU and 32GB of RAM, running on `Release`. Each test sends 5 gigabytes of data over the loopback connection.

The results vary depending on the version of .NET used. Here are the results of the benchmarks, sorted in descending throughput:

| .NET Version | Packet Size | Packets Per Second | Throughput |
| ------------ | ------------ | ------------------ | ---------- |
| .NET 6.0.0-rtm.21522.10 | 131,072 | 6,389.41 msg/s | 837.47 MB/s |
| .NET 5.0.11 | 65,536 | 12,769.34 msg/s | 836.85 MB/s |
| .NET Core 3.1.21 | 65,536 | 12,328.76 msg/s | 807.98 MB/s |
| .NET 6.0.0-rtm.21522.10 | 65,536 | 12,270.52 msg/s | 804.16 MB/s |
| .NET 5.0.11 | 131,072 | 6,021.29 msg/s | 789.22 MB/s |
| .NET Core 3.1.21 | 131,072 | 6,051.64 msg/s | 793.20 MB/s |
| .NET Core 3.1.21 | 262,144 | 2,944.58 msg/s | 771.88 MB/s |
| .NET 5.0.11 | 262,144 | 2,905.69 msg/s | 761.69 MB/s |
| .NET 6.0.0-rtm.21522.10 | 262,144 | 2,841.93 msg/s | 744.97 MB/s |
| .NET 6.0.0-rtm.21522.10 | 8,192 | 78,116.06 msg/s | 639.93 MB/s |
| .NET 5.0.11 | 8,192 | 71,034.53 msg/s | 581.91 MB/s |
| .NET Core 3.1.21 | 8,192 | 62,709.79 msg/s | 513.72 MB/s |
| .NET Framework 4.8.4420.0 | 131,072 | 1,094.11 msg/s | 143.41 MB/s |
| .NET Framework 4.8.4420.0 | 262,144 | 527.24 msg/s | 138.21 MB/s |
| .NET Framework 4.8.4420.0 | 65,536 | 2,097.67 msg/s | 137.47 MB/s |
| .NET Framework 4.8.4420.0 | 8,192 | 14,423.41 msg/s | 118.16 MB/s |



*Results may fluctuate based on CPU Usage, GC, and other variables. These numbers are based on a single run.*