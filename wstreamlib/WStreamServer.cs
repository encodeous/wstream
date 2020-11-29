
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace wstreamlib
{
    public class WStreamServer
    {
        public ConcurrentDictionary<Guid, WsConnection> ActiveConnections;
        public bool IsListening { get; private set; }
        private IHost _host;

        public WStreamServer()
        {
            ActiveConnections = new ConcurrentDictionary<Guid, WsConnection>();
        }

        public Task Listen(IPEndPoint endpoint, X509Certificate2 sslCert = null)
        {
            IsListening = true;
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStaticWebAssets()
                        .ConfigureServices(x => x.AddSingleton(this))
                        .UseStartup<WsStartup>()
                        .ConfigureKestrel(options =>
                        {
                            options.Listen(endpoint, o =>
                            {
                                o.Protocols = HttpProtocols.Http1;
                                if (sslCert != null)
                                {
                                    o.UseHttps(x =>
                                    {
                                        x.ServerCertificate = sslCert;
                                        x.SslProtocols = SslProtocols.Tls13;
                                    });
                                }
                                
                            });
                        }).ConfigureLogging(x=>x.ClearProviders());
                }).Build();
            return _host.RunAsync();
        }

        public void Stop()
        {
            _host.StopAsync();
            if (IsListening)
            {
                foreach (WsConnection connection in ActiveConnections.Values)
                {
                    try
                    {
                        if (connection.Connected)
                        {
                            connection.Close();
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            IsListening = false;
        }

        public delegate void NewConnection(WsConnection connection);

        public event NewConnection ConnectionAddedEvent;

        internal void AddConnection(WsConnection connection)
        {
            while (!ActiveConnections.TryAdd(connection.ConnectionId, connection))
            {

            }
            ConnectionAddedEvent?.Invoke(connection);
        }

        internal void ConnectionClosed(WsConnection connection)
        {
            if (ActiveConnections.ContainsKey(connection.ConnectionId))
            {
                while (!ActiveConnections.TryRemove(connection.ConnectionId, out _))
                {

                }
            }
        }

        ~WStreamServer()
        {
            Stop();
        }
    }
}