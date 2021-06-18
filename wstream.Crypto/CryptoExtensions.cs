﻿using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
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
        /// <summary>
        /// Generates a new ECDSA private / public key pair using the secp384r1 curve
        /// </summary>
        /// <returns>A new ECDSA key</returns>
        public static ECParameters GenerateKey()
        {
            var ecdsa = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp384r1"));
            return ecdsa.ExportParameters(true);
        }

        /// <summary>
        /// Exports the ECDSA key in Pkcs8 format
        /// </summary>
        /// <param name="parameters">The ECDSA key</param>
        /// <returns>Exported private key</returns>
        public static byte[] ExportPrivateKey(ECParameters parameters)
        {
            ECDsa ecDsa = ECDsa.Create(parameters);
            return ecDsa.ExportPkcs8PrivateKey();
        }

        /// <summary>
        /// Imports the ECDSA key from Pkcs8 format
        /// </summary>
        /// <param name="bytes">Private key store in the Pkcs8 format</param>
        /// <returns>The ECDSA key</returns>
        public static ECParameters ImportPrivateKey(byte[] bytes)
        {
            ECDsa ecDsa = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp384r1"));
            ecDsa.ImportPkcs8PrivateKey(bytes, out var _);
            return ecDsa.ExportParameters(true);
        }
        /// <summary>
        /// Returns the Hex-encoded value of the x-coordinate of the public key
        /// </summary>
        /// <param name="publicKey">The hex-encoded value</param>
        /// <returns></returns>
        public static string GetFingerprintString(this ECPoint publicKey)
        {
            return BitConverter.ToString(publicKey.X);
        }

        /// <summary>
        /// Establishes encryption in the current socket. It is required that both the client and server call this!
        /// <remarks>
        /// This method overload will create a random ECDSA signing key every time it is called. It is highly recommended to use fixed keys and validate the returned fingerprint with a trusted store.
        /// </remarks>
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static Task<ECPoint> EncryptAsync(this WsStream stream)
        {
            return EncryptAsync(stream, ECDsa.Create(ECCurve.CreateFromFriendlyName("secp384r1")).ExportParameters(true));
        }
        /// <summary>
        /// Establishes encryption in the current socket. It is required that both the client and server call this!
        /// </summary>
        /// <remarks>
        /// It is highly recommended to validate the returned fingerprint with a trusted store.
        /// </remarks>
        /// <param name="stream"></param>
        /// <param name="parameters">ECDSA public / private keypair used for signing</param>
        /// <returns>The fingerprint (public key) of the remote</returns>
        public static async Task<ECPoint> EncryptAsync(this WsStream stream, ECParameters parameters)
        {
            var (secret, fingerprint) = await ExchangeKeyAsync(stream, parameters);
            // wrap stream
            await stream.WrapSocketAsync(x => 
                Task.FromResult<WStreamSocket>(new WStreamCryptoSocket(x, secret)));
            return fingerprint;
        }

        /// <summary>
        /// Securely exchanges a secret key
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="ecParams">ECDSA public / private keypair used for signing</param>
        /// <returns>A tuple containing a 256 bit hashed secret key, and the fingerprint of the remote</returns>
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="InvalidDataException">Thrown when the remote sends invalid data</exception>
        public static async Task<(byte[], ECPoint)> ExchangeKeyAsync(this WsStream stream, ECParameters ecParams)
        {
            if (ecParams.D is null) throw new CryptographicException("Private key must be provided");
            ECDsa ecDsa = ECDsa.Create(ecParams);
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
            var remotePubBytes = await br.ReadBytesAsync(await br.ReadAssertAsync(120));
            remotePubKey.ImportSubjectPublicKeyInfo(remotePubBytes, out _);
            //2
            var remoteSignature = await br.ReadBytesAsync(await br.ReadAssertAsync(96));
            //3
            var remoteKePub = await br.ReadBytesAsync(await br.ReadAssertAsync(158));

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
        
        /// <exception cref="InvalidDataException"></exception>
        private static async Task<int> ReadAssertAsync(this AsyncBinaryReader br, int expected)
        {
            int x = await br.ReadInt32Async();
            if (x != expected) throw new InvalidDataException("The remote sent an unexpected result.");
            return x;
        }
        
    }
}