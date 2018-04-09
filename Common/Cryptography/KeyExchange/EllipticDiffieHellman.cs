using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Common.Cryptography.KeyExchange
{
    public class EllipticDiffieHellman : IKeyExchange
    {
        private static readonly BigInteger c_25519_prime = (BigInteger.One << 255) - 19;
        private static readonly BigInteger c_25519_order = (BigInteger.One << 252) + BigInteger.Parse("27742317777372353535851937790883648493"); // 27_742_317_777_372_353_535_851_937_790_883_648_493
        private static readonly EllipticCurve c_25519 = new EllipticCurve(486662, 1, c_25519_prime, EllipticCurve.CurveType.Montgomery);
        private static readonly Point c_25519_gen = new Point(9, BigInteger.Parse("14781619447589544791020593568409986887264606134616475288964881837755586237401"));

        protected static readonly Random rand = new Random();

        protected readonly EllipticCurve curve;
        public readonly BigInteger priv;
        protected readonly Point generator, pub;


        public EllipticDiffieHellman(EllipticCurve curve, Point generator, BigInteger order, byte[] priv = null)
        {
            this.curve = curve;
            this.generator = generator;

            // Generate private key
            if (priv == null)
            {
                byte[] max = order.ToByteArray();
                do
                {
                    byte[] p1 = new byte[5 /*rand.Next(max.Length) + 1*/];

                    rand.NextBytes(p1);

                    if (p1.Length == max.Length) p1[p1.Length - 1] %= max[max.Length - 1];
                    else p1[p1.Length - 1] &= 127;

                    this.priv = new BigInteger(p1);
                } while (this.priv < 2);
            }
            else this.priv = new BigInteger(priv);

            // Generate public key
            pub = curve.Multiply(generator, this.priv);
        }

        public byte[] GetPublicKey()
        {
            byte[] p1 = pub.X.ToByteArray();
            byte[] p2 = pub.Y.ToByteArray();

            byte[] ser = new byte[4 + p1.Length + p2.Length];
            ser[0] = (byte)(p1.Length & 255);
            ser[1] = (byte)((p1.Length >> 8) & 255);
            ser[2] = (byte)((p1.Length >> 16) & 255);
            ser[3] = (byte)((p1.Length >> 24) & 255);
            Array.Copy(p1, 0, ser, 4, p1.Length);
            Array.Copy(p2, 0, ser, 4 + p1.Length, p2.Length);

            return ser;
        }

        public byte[] GetPrivateKey() => priv.ToByteArray();

        public byte[] GetSharedSecret(byte[] pK)
        {
            byte[] p1 = new byte[pK[0] | (pK[1] << 8) | (pK[2] << 16) | (pK[3] << 24)]; // Reconstruct x-axis size
            byte[] p2 = new byte[pK.Length - p1.Length - 4];
            Array.Copy(pK, 4, p1, 0, p1.Length);
            Array.Copy(pK, 4 + p1.Length, p2, 0, p2.Length);

            Point remotePublic = new Point(new BigInteger(p1), new BigInteger(p2));

            return curve.Multiply(remotePublic, priv).X.ToByteArray(); // Use the x-coordinate as the shared secret
        }

        public static EllipticDiffieHellman Curve25519(BigInteger priv) => new EllipticDiffieHellman(c_25519, c_25519_gen, c_25519_order, priv.ToByteArray());
        public static BigInteger Curve25519_GeneratePrivate(RandomProvider provider) => Support.GenerateRandom(provider, c_25519_order - 2) + 2;
    }
}
