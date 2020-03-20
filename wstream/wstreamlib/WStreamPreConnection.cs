using System;
using System.Collections.Generic;
using System.Text;
using wstreamlib.Ninja.WebSockets;

namespace wstreamlib
{
    public class WStreamPreConnection
    {
        public WebSocketHttpContext Context { get; internal set; }
        public bool IsCancelled { get; internal set; }

        public void Cancel()
        {
            IsCancelled = true;
        }
    }
}
