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
        [Theory]
        [Repeat(10)]
        public async Task ValidateCryptoHandshake(int i)
        {
            var server = new WsServer();
            var serverKey = CryptoExtensions.GenerateKey();
            var clientKey = CryptoExtensions.GenerateKey();
            ECPoint serverPub, clientPub = new ECPoint();
            await server.StartAsync(new IPEndPoint(IPAddress.Loopback, 8082), async stream =>
            {
                clientPub = await stream.EncryptAsync(serverKey);
            });

            // start client
            var client = new WsClient();
            var conn = await client.ConnectAsync(new Uri($"ws://localhost:8082"));
            serverPub = await conn.EncryptAsync(clientKey);

            await Task.Delay(1000);
            
            Assert.Equal(serverKey.Q.X, serverPub.X);
            Assert.Equal(clientKey.Q.X, clientPub.X);
            server.Dispose();
            client.Dispose();
        }
    }
}