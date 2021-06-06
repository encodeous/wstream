using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace wstream.Crypto
{
    internal struct CryptoPacketState
    {
        public int BufferLength;
        public byte[] Tag;
        public byte[] Buffer;
        public int Position;
    }
    public class WStreamCryptoSocket : WStreamSocket
    {
        private AesGcm _aes;
        private byte[] _readNonce;
        private byte[] _writeNonce;
        private CryptoPacketState _currentPacket;
        public WStreamCryptoSocket(WStreamSocket baseSocket, byte[] sharedSecret) : base(baseSocket)
        {
            _readNonce = new byte[12];
            _writeNonce = new byte[12];
            // allow concurrent read/write without nonce duplication
            if (Parity)
            {
                _readNonce[11] = 255;
            }
            else
            {
                _writeNonce[11] = 255;
            }
            _aes = new AesGcm(sharedSecret);
            _currentPacket.Buffer = null;
            _currentPacket.Tag = new byte[16];
        }
        
        private static void IncrementNonce(byte[] nonce)
        {
            var j = 0;
            while (j < nonce.Length && ++nonce[j++] == 0) { }
        }

        public override async Task<int> ReadAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken = new CancellationToken())
        {
            // TODO: Harden security (prevent abuse, double check everything)
            int len = buffer.Count;
            int rem = 0;
            if (_currentPacket.Buffer is not null)
            {
                rem = _currentPacket.BufferLength - _currentPacket.Position;
            }

            // check if remaining crypto buffer is enough to fill read buffer
            if (rem <= 0)
            {
                // not enough
                if (_currentPacket.Buffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(_currentPacket.Buffer);
                }

                // read packet
                _currentPacket.BufferLength = await ReadIntAsync(cancellationToken);
                _currentPacket.Position = 0;
                // read tag
                await ReadBytesAsync(_currentPacket.Tag, cancellationToken);
                // read whole buffer (required)
                _currentPacket.Buffer = ArrayPool<byte>.Shared.Rent(_currentPacket.BufferLength);
                var tBuf = ArrayPool<byte>.Shared.Rent(_currentPacket.BufferLength);
                var tBufSeg = new ArraySegment<byte>(tBuf, 0, _currentPacket.BufferLength);
                await ReadBytesAsync(tBufSeg, cancellationToken);
                // decrypt aes
                var uBufSeg = new ArraySegment<byte>(_currentPacket.Buffer, 0, _currentPacket.BufferLength);
                _aes.Decrypt(_readNonce, tBufSeg, _currentPacket.Tag, uBufSeg);
                IncrementNonce(_readNonce);
                ArrayPool<byte>.Shared.Return(tBuf);
                return await ReadAsync(buffer, cancellationToken);
            }
            
            // fill buffer and advance position
            new ArraySegment<byte>(_currentPacket.Buffer, _currentPacket.Position, Math.Min(len, rem)).CopyTo(buffer);
            _currentPacket.Position += Math.Min(len, rem);
            return rem;
        }

        private byte[] _rBuf = new byte[4];

        private async ValueTask<int> ReadIntAsync(CancellationToken ct)
        {
            await ReadBytesAsync(_rBuf, ct);
            return BitConverter.ToInt32(_rBuf);
        }

        private async Task ReadBytesAsync(ArraySegment<byte> result, CancellationToken cancellationToken = default(CancellationToken))
        {
            int count = result.Count;
            do
            {
                int num = await WrappedSocket.ReadAsync(result, cancellationToken).ConfigureAwait(false);
                if (num != 0)
                {
                    count -= num;
                }
                else
                {
                    throw new EndOfStreamException("Unexpected end of stream while trying to read crypto packet!");
                }
            } while (count > 0);
        }

        public override Task WriteAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            var buf = ArrayPool<byte>.Shared.Rent(buffer.Count + 20);
            var ctext = new ArraySegment<byte>(buf, 20, buffer.Count);
            var tag = new ArraySegment<byte>(buf, 4, 16);
            BitConverter.TryWriteBytes(new ArraySegment<byte>(buf, 0, 4), buffer.Count);
            lock (_writeNonce)
            {
                _aes.Encrypt(_writeNonce, buffer, ctext, tag);
                IncrementNonce(_writeNonce);
            }

            async Task Send()
            {
                await WrappedSocket.WriteAsync(new ArraySegment<byte>(buf, 0, buffer.Count + 20), cancellationToken);
                ArrayPool<byte>.Shared.Return(buf);
            }

            return Send();
        }
    }
}