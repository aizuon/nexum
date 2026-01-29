using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1;

namespace Nexum.Core.Crypto
{
    internal static class RSAHelper
    {
        internal static RSA CreateRsaFromPublicKey(string publicKey)
        {
            byte[] publicKeyBytes = Convert.FromBase64String(publicKey);

            var seq = (DerSequence)Asn1Object.FromByteArray(publicKeyBytes);

            var parameters = new RSAParameters
            {
                Exponent = ((DerInteger)seq[1]).Value.ToByteArrayUnsigned(),
                Modulus = ((DerInteger)seq[0]).Value.ToByteArrayUnsigned()
            };

            var rsa = RSA.Create();
            rsa.ImportParameters(parameters);

            return rsa;
        }
    }
}
