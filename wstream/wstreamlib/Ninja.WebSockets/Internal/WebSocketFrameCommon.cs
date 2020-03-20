// ---------------------------------------------------------------------
// Copyright 2018 David Haig
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
// ---------------------------------------------------------------------

using System;

namespace wstreamlib.Ninja.WebSockets.Internal
{
    internal static class WebSocketFrameCommon
    {
        public const int MaskKeyLength = 4;

        internal static readonly bool is64 = Environment.Is64BitProcess;
        /// <summary>
        /// Mutate payload with the mask key
        /// This is a reversible process
        /// If you apply this to masked data it will be unmasked and visa versa
        /// </summary>
        /// <param name="maskKey">The 4 byte mask key</param>
        /// <param name="payload">The payload to mutate</param>
        public static void ToggleMask(ArraySegment<byte> maskKey, ArraySegment<byte> payload)
        {
            if (maskKey.Count != MaskKeyLength)
            {
                throw new Exception($"MaskKey key must be {MaskKeyLength} bytes");
            }
            // apply the mask key (this is a reversible process so no need to copy the payload)
            // NOTE: this is a hot function
            //for (int i = payloadOffset; i < payloadCountPlusOffset; i++)
            //{
            //    int payloadIndex = i - payloadOffset; // index should start at zero
            //    int maskKeyIndex = maskKeyOffset + (payloadIndex % MaskKeyLength);
            //    buffer[i] = (Byte)(buffer[i] ^ maskKeyArray[maskKeyIndex]);
            //}
            if (is64)
            {
                ToggleMask64(maskKey.Array, maskKey.Offset, payload.Array,
                    payload.Offset, payload.Count);
            }
            else
            {
                ToggleMask32(maskKey.Array, maskKey.Offset, payload.Array,
                    payload.Offset, payload.Count);
            }
        }
        public static unsafe void ToggleMask32(byte[] key, int maskKeyOffset, byte[] payload, int payloadOffset, int payloadLength)
        {
            int chunks = payloadLength / 4;
            fixed (byte* keyBytes = key)
            {
                fixed (byte* payloadBytes = payload)
                {
                    int* key32 = (int*)keyBytes;
                    int* bytes32 = (int*)payloadBytes;
                    key32 += maskKeyOffset;
                    bytes32 += payloadOffset;
                    for (int p = 0; p < chunks; p++)
                    {
                        *bytes32 ^= *key32;
                        bytes32++;
                    }
                }
            }
            for (int index = chunks * 4; index < payloadLength; index++)
            {
                payload[index] ^= key[index % 4];
            }
        }
        public static unsafe void ToggleMask64(byte[] key, int maskKeyOffset, byte[] payload, int payloadOffset, int payloadLength)
        {
            byte* keyDup = stackalloc byte[8];
            for (int i = 0; i < 8; i++)
            {
                keyDup[i] = key[(i % 4) + maskKeyOffset];
            }
            int chunks = payloadLength / 8;
            fixed (byte* payloadBytes = payload)
            {
                long* key64 = (long*)keyDup;
                long* bytes64 = (long*)payloadBytes;
                bytes64 += payloadOffset;
                for (int p = 0; p < chunks; p++)
                {
                    *bytes64 ^= *key64;
                    bytes64++;
                }
            }
            for (int index = chunks * 8; index < payloadLength; index++)
            {
                payload[index] ^= keyDup[index % 8];
            }
        }
    }
}
