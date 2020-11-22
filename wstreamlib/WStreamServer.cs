
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace wstreamlib
{
    public class WStreamServer
    {
        public Dictionary<Guid, WsConnection> ActiveConnections;
        public bool IsListening { get; private set; }
        private IHost _host;

        public WStreamServer()
        {
            ActiveConnections = new Dictionary<Guid, WsConnection>();
        }

        public void Listen(IPEndPoint endpoint)
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
                            options.Listen(endpoint, o => o.Protocols = HttpProtocols.Http1);
                        });
                }).Build();
            _host.RunAsync();
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
            ActiveConnections.Add(connection.ConnectionId, connection);
            ConnectionAddedEvent?.Invoke(connection);
        }

        private void ConnectionClosedEvent(WsConnection connection)
        {
            if (ActiveConnections.ContainsKey(connection.ConnectionId))
            {
                ActiveConnections.Remove(connection.ConnectionId);
            }
        }

        ~WStreamServer()
        {
            Stop();
        }
    }
}