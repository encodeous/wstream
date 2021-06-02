﻿using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;

namespace wstream.Crypto
{
    /// <summary>
    /// <para>
    /// SSH-Like encryption
    /// </para>
    /// <para>
    /// WARNING: This software is not guaranteed to be secure. This is just an experiment, use with care!
    /// </para>
    /// <para>
    /// Ciphersuite: ECDH (Key-Exchange) + ECDSA (Fingerprinting) + AES256-GCM (Stream Cipher) + SHA256
    /// </para>
    /// An ephemeral ECDH(E) keypair is generated by the client and server every connection, and the public (Key-Exchange) key is signed with their corresponding fingerprints.
    /// This allows the protocol to validate the identity of the client and server, and prevents MITM attacks.
    /// </summary>
    public static class CryptoExtensions
    {
        public static Task<ECPoint> EncryptAsync(this WsStream stream)
        {
            return EncryptAsync(stream, ECDsa.Create(ECCurve.CreateFromFriendlyName("secp384r1")));
        }
        public static Task<ECPoint> EncryptAsync(this WsStream stream, ECParameters parameters)
        {
            ECDsa ecDsa = ECDsa.Create(parameters);
            return EncryptAsync(stream, ecDsa);
        }
        
        public static async Task<ECPoint> EncryptAsync(this WsStream stream, ECDsa parameters)
        {
            var (secret, fingerprint) = await ExchangeKeyAsync(stream, parameters);
            // wrap stream
            await stream.WrapSocketAsync(x => 
                Task.FromResult<WStreamSocket>(new WStreamCryptoSocket(x, secret)));
            return fingerprint;
        }

        private static async Task<(byte[], ECPoint)> ExchangeKeyAsync(this WsStream stream, ECDsa ecDsa)
        {
            // TODO: Harden security (prevent abuse, double check everything)
            // host authentication
            var pubBytes = ecDsa.ExportSubjectPublicKeyInfo();
            
            // key exchange
            var ecdh = ECDiffieHellman.Create();
            var kePubBytes = ecdh.ExportSubjectPublicKeyInfo();
            
            // sign ecdh key to authenticate
            var signed = ecDsa.SignData(kePubBytes, HashAlgorithmName.SHA256);
            
            var bw = new AsyncBinaryWriter(stream);
            var br = new AsyncBinaryReader(stream);
            //1
            await bw.WriteAsync(pubBytes.Length);
            await bw.WriteAsync(pubBytes);
            //2
            await bw.WriteAsync(signed.Length);
            await bw.WriteAsync(signed);
            //3
            await bw.WriteAsync(kePubBytes.Length);
            await bw.WriteAsync(kePubBytes);
            
            // read remote public key and verify signature
            //1
            var remotePubKey = ECDsa.Create();
            var remotePubBytes = await br.ReadBytesAsync(await br.ReadInt32Async());
            remotePubKey.ImportSubjectPublicKeyInfo(remotePubBytes, out _);
            //2
            var remoteSignature = await br.ReadBytesAsync(await br.ReadInt32Async());
            //3
            var remoteKePub = await br.ReadBytesAsync(await br.ReadInt32Async());

            var remoteEcdh = ECDiffieHellman.Create();
            remoteEcdh.ImportSubjectPublicKeyInfo(remoteKePub, out _);
            
            // verify signed public key exchange key
            if (!remotePubKey.VerifyData(remoteKePub, remoteSignature, HashAlgorithmName.SHA256))
            {
                throw new CryptographicException("Remote public key does not match hash!");
            }
            // derive shared secret
            var sharedSecret = ecdh.DeriveKeyMaterial(remoteEcdh.PublicKey); 
            // return the public key (fingerprint) of the remote, and the hashed shared secret
            return (SHA256.HashData(sharedSecret), remotePubKey.ExportParameters(false).Q);
        }
    }
}