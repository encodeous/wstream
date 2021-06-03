﻿using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Flurl.Http;
using Xunit;
using Xunit.Repeat;

namespace wstream.Tests
{
    public class HttpRequestTestFixture : IDisposable
    {
        private WsServer _server;

        public string ExpectedResult;
        
        public HttpRequestTestFixture()
        {
            ExpectedResult = "WStream.Tests - HttpRequestTest";
            _server = new WsServer();
            _server.StartAsync(new IPEndPoint(IPAddress.Loopback, 8083), async stream 
                => throw new Exception("Client should not have been able to connect!"), defaultPage:ExpectedResult).GetAwaiter().GetResult();
        }
        public void Initialize(string s)
        {
            
        }
        public void Dispose()
        {
            _server.Dispose();
        }
    }
    public class HttpRequestTest : IClassFixture<HttpRequestTestFixture>
    {
        private HttpRequestTestFixture _fixture;
        public HttpRequestTest(HttpRequestTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void TestServer()
        {
            var res = Parallel.For(0, 100, async (x) =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var result = await "http://localhost:8083".GetStringAsync();
                    Assert.Equal(_fixture.ExpectedResult, result);
                }
            });
            Assert.True(res.IsCompleted);
        }
    }
}