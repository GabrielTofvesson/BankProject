using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Tofvesson.Common.Cryptography.KeyExchange
{
    public sealed class DiffieHellman : IKeyExchange
    {
        private static readonly BigInteger EPHEMERAL_MAX = BigInteger.One << 2048;
        private static readonly RandomProvider provider = new CryptoRandomProvider();
        private BigInteger priv, p, q;
        private readonly BigInteger pub;

        public DiffieHellman(BigInteger p, BigInteger q) : this(provider.GenerateRandom(EPHEMERAL_MAX), p, q) { }
        public DiffieHellman(BigInteger priv, BigInteger p, BigInteger q)
        {
            this.priv = priv;
            this.p = p;
            this.q = q;
            pub = Support.ModExp(p, priv, q);
        }

        public byte[] GetPublicKey() => pub.ToByteArray();

        public byte[] GetSharedSecret(byte[] p) {
            BigInteger pub = new BigInteger(p);
            return (pub <= 0 ? (BigInteger) 0 : Support.ModExp(pub, priv, q)).ToByteArray();
        }
    }
}
