﻿using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using wstream;
using wstream.Crypto;

namespace samples
{
    public class WsCryptoTest
    {
        public async Task Test()
        {
            // create the server
            var server = new WsServer();
            await server.StartAsync(new IPEndPoint(IPAddress.Any, 8080), async stream =>
            {
                // encrypt
                // called when a client connects
                var br = new BinaryWriter(stream);
                var ct = Encoding.ASCII.GetBytes($"Hello from Server! {DateTime.Now}");
                br.Write(ct.Length);
                br.Write(ct);
                await stream.EncryptAsync();
                while (stream.Connected)
                {
                    var bytes = Encoding.ASCII.GetBytes($"Hello from Server! {DateTime.Now}");
                    br.Write(bytes.Length);
                    br.Write(bytes);
                    await Task.Delay(1000);
                }
                await stream.CloseAsync();
            });

            // start client
            var client = new WsClient();
            var connection = await client.ConnectAsync(new Uri("ws://localhost:8080"));
            // measure client data
            
            // read data
            var binReader = new BinaryReader(connection);
            int e = binReader.ReadInt32();
            var g = binReader.ReadBytes(e);
            Console.WriteLine(Encoding.ASCII.GetString(g));
            
            await connection.EncryptAsync();
            
            for (int i = 0; i < 100; i++)
            {
                int b = binReader.ReadInt32();
                var bytesRead = binReader.ReadBytes(b);
                Console.WriteLine(Encoding.ASCII.GetString(bytesRead));
            }
            // cleanup
            client.Dispose();
            server.Dispose();
        }
    }
}