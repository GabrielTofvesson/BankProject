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

            byte[] secret = curve.Multiply(remotePublic, priv).X.ToByteArray(); // Use the x-coordinate as the shared secret

            // SHA-1 (Common shared secret generation method)

            // Initialize buffers
            uint h0 = 0x67452301;
            uint h1 = 0xEFCDAB89;
            uint h2 = 0x98BADCFE;
            uint h3 = 0x10325476;
            uint h4 = 0xC3D2E1F0;

            // Pad message
            int ml = secret.Length + 1;
            byte[] msg = new byte[ml + ((960 - (ml * 8 % 512)) % 512) / 8 + 8];
            Array.Copy(secret, msg, secret.Length);
            msg[secret.Length] = 0x80;
            long len = secret.Length * 8;
            for (int i = 0; i < 8; ++i) msg[msg.Length - 1 - i] = (byte)((len >> (i * 8)) & 255);
            //Support.WriteToArray(msg, message.Length * 8, msg.Length - 8);
            //for (int i = 0; i <4; ++i) msg[msg.Length - 5 - i] = (byte)(((message.Length*8) >> (i * 8)) & 255);

            int chunks = msg.Length / 64;

            // Perform hashing for each 512-bit block
            for (int i = 0; i < chunks; ++i)
            {

                // Split block into words
                uint[] w = new uint[80];
                for (int j = 0; j < 16; ++j)
                    w[j] |= (uint)((msg[i * 64 + j * 4] << 24) | (msg[i * 64 + j * 4 + 1] << 16) | (msg[i * 64 + j * 4 + 2] << 8) | (msg[i * 64 + j * 4 + 3] << 0));

                // Expand words
                for (int j = 16; j < 80; ++j)
                    w[j] = Rot(w[j - 3] ^ w[j - 8] ^ w[j - 14] ^ w[j - 16], 1);

                // Initialize chunk-hash
                uint
                    a = h0,
                    b = h1,
                    c = h2,
                    d = h3,
                    e = h4;

                // Do hash rounds
                for (int t = 0; t < 80; ++t)
                {
                    uint tmp = ((a << 5) | (a >> (27))) +
                        ( // Round-function
                        t < 20 ? (b & c) | ((~b) & d) :
                        t < 40 ? b ^ c ^ d :
                        t < 60 ? (b & c) | (b & d) | (c & d) :
                        /*t<80*/ b ^ c ^ d
                        ) +
                        e +
                        ( // K-function
                        t < 20 ? 0x5A827999 :
                        t < 40 ? 0x6ED9EBA1 :
                        t < 60 ? 0x8F1BBCDC :
                        /*t<80*/ 0xCA62C1D6
                        ) +
                        w[t];
                    e = d;
                    d = c;
                    c = Rot(b, 30);
                    b = a;
                    a = tmp;
                }
                h0 += a;
                h1 += b;
                h2 += c;
                h3 += d;
                h4 += e;
            }

            return WriteContiguous(new byte[20], 0, SwapEndian(h0), SwapEndian(h1), SwapEndian(h2), SwapEndian(h3), SwapEndian(h4));
        }
        private static uint Rot(uint val, int by) => (val << by) | (val >> (32 - by));

        // Swap endianness of a given integer
        private static uint SwapEndian(uint value) => (uint)(((value >> 24) & (255 << 0)) | ((value >> 8) & (255 << 8)) | ((value << 8) & (255 << 16)) | ((value << 24) & (255 << 24)));

        private static byte[] WriteToArray(byte[] target, uint data, int offset)
        {
            for (int i = 0; i < 4; ++i)
                target[i + offset] = (byte)((data >> (i * 8)) & 255);
            return target;
        }

        private static byte[] WriteContiguous(byte[] target, int offset, params uint[] data)
        {
            for (int i = 0; i < data.Length; ++i) WriteToArray(target, data[i], offset + i * 4);
            return target;
        }

        public static EllipticDiffieHellman Curve25519(BigInteger priv) => new EllipticDiffieHellman(c_25519, c_25519_gen, c_25519_order, priv.ToByteArray());
        public static BigInteger Curve25519_GeneratePrivate(RandomProvider provider) => Support.GenerateRandom(provider, c_25519_order - 2) + 2;
    }
}
