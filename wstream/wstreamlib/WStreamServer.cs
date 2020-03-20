
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using wstreamlib.Ninja.WebSockets;

namespace wstreamlib
{
    public class WStreamServer
    {
        public Dictionary<Guid, WsConnection> ActiveConnections;
        private readonly WebSocketServerFactory _factory;
        private X509Certificate2 _cert;

        public TcpListener Listener;
        public delegate void WStreamPreConnectionDelegate(WStreamPreConnection connection);
        public event WStreamPreConnectionDelegate PreConnectionEvent;
        public string ErrorResponseMessage;
        public bool IsListening { get; private set; }

        public WStreamServer()
        {
            ActiveConnections = new Dictionary<Guid, WsConnection>();
            _factory = new WebSocketServerFactory();
            ErrorResponseMessage =
                $"HTTP/1.1 404 Not Found\r\nDate: {DateTime.Now.ToUniversalTime():r}\r\nServer: wstream\r\n\r\n{Config.Version}";
        }

        public void Listen(IPEndPoint endpoint, X509Certificate2 certificate = null)
        {
            _cert = certificate;
            Listener = new TcpListener(endpoint);
            Listener.Start();
            IsListening = true;
        }

        public void Stop()
        {
            if (IsListening)
            {
                Listener.Stop();
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

        public async Task<WsConnection> AcceptConnectionAsync()
        {
            while (IsListening)
            {
                Socket sock = await Listener.AcceptSocketAsync().ConfigureAwait(false);
                Stream stream = new NetworkStream(sock);
                if (_cert != null)
                {
                    stream = new SslStream(stream,false);
                    await ((SslStream) stream).AuthenticateAsServerAsync(_cert,false, true).ConfigureAwait(false);
                }
                WebSocketHttpContext context = await _factory.ReadHttpHeaderFromStreamAsync(stream).ConfigureAwait(false);
                var eventArg = new WStreamPreConnection {Context = context};
                if (context.IsWebSocketRequest)
                {
                    PreConnectionEvent?.Invoke(eventArg);
                    if (!eventArg.IsCancelled)
                    {
                        WebSocket wsi = await _factory.AcceptWebSocketAsync(context).ConfigureAwait(false);
                        var conn = new WsConnection(wsi);
                        conn.OnConnectionClosed += OnConnectionClosed;
                        ActiveConnections[conn.ConnectionId] = conn;
                        return conn;
                    }

                    stream.Close();
                    sock.Close();
                    continue;
                }
                stream.Write(Encoding.UTF8.GetBytes(ErrorResponseMessage));
                stream.Close();
                sock.Close();
            }

            return null;
        }

        private void OnConnectionClosed(Guid id)
        {
            if (ActiveConnections.ContainsKey(id))
            {
                ActiveConnections.Remove(id);
            }
        }

        ~WStreamServer()
        {
            Stop();
        }
    }
}