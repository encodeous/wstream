using System;
using samples;

await new PingTestSimple().Test();
await new WsWrapTest().Test();
await new PingTest().Test();