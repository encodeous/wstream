using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;
using wstream.Crypto;
using Xunit;
using Xunit.Repeat;

namespace wstream.Tests
{
    public class CryptoHandshakeTest
    {
        [Fact]
        public async Task ValidateCryptoHandshake()
        {
            var server = new WsServer();
            var serverKey = CryptoExtensions.GenerateKey();
            var clientKey = CryptoExtensions.GenerateKey();
            ECPoint serverPub, clientPub = new ECPoint();
            await server.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), async stream =>
            {
                clientPub = await stream.EncryptAsync(serverKey);
            });
            
            await Task.Delay(500);
            
            // start client
            var client = new WsClient();
            var conn = await client.ConnectAsync(new Uri($"ws://"+server.ListeningAddresses[0].Substring(7)));
            serverPub = await conn.EncryptAsync(clientKey);

            await Task.Delay(500);
            
            Assert.Equal(serverKey.Q.X, serverPub.X);
            Assert.Equal(clientKey.Q.X, clientPub.X);
            server.Dispose();
            client.Dispose();
        }
    }
}