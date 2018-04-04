using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Tofvesson.Crypto
{
    public class RSA
    {
        private static readonly PassthroughPadding NO_PADDING = new PassthroughPadding();
        private static readonly Encoding DEFAULT_ENCODING = Encoding.UTF8;

        private readonly RandomProvider provider = new CryptoRandomProvider();

        private readonly BigInteger e;
        private readonly BigInteger n;
        private readonly BigInteger d;

        public bool CanEncrypt { get; private set; }
        public bool CanDecrypt { get; private set; }

        public RSA(int byteSize, int margin, int threads, int certainty)
        {
            // Choose primes
            BigInteger p = Support.GeneratePrime(threads, byteSize, margin, certainty, provider);
            BigInteger q = Support.GeneratePrime(threads, byteSize, margin, certainty, provider);


            // For optimization
            BigInteger p_1 = p - 1;
            BigInteger q_1 = q - 1;

            // Calculate needed values
            n = p * q;
            BigInteger lcm = (p_1 * q_1) / Support.GCD(p_1, q_1);

            // Generate e such that is is less than and coprime to lcm
            do
            {
                e = RandomSupport.GenerateBoundedRandom(lcm, provider);
            } while (e == lcm || Support.GCD(e, lcm) != 1);

            // Generate the modular multiplicative inverse
            d = Support.Dio(e, lcm).Key + lcm;
            CanEncrypt = true;
            CanDecrypt = true;
        }

        // Load necessary values from files
        public RSA(string e_file, string n_file, string d_file) : this(File.ReadAllBytes(e_file), File.ReadAllBytes(n_file), File.ReadAllBytes(d_file))
        { }

        public RSA(byte[] e, byte[] n, byte[] d = null)
        {
            this.e = new BigInteger(e);
            this.n = new BigInteger(n);
            this.d = new BigInteger(d ?? new byte[0]);
            CanEncrypt = true;
            CanDecrypt = d!=null;
        }

        // Create a shallow copy of the given object because it's really just wasteful to perform a deep copy
        // unless the person modifying this code is a madman, in which case I highly doubt they'd willfully leave this code alone anyway...
        public RSA(RSA copy)
        {
            e = copy.e;
            n = copy.n;
            d = copy.d;
            CanEncrypt = copy.CanEncrypt;
            CanDecrypt = copy.CanDecrypt;
        }

        // Create a "remote" instance of the rsa object. This means that we do not know the private exponent
        private RSA(byte[] e, byte[] n)
        {
            this.e = new BigInteger(e);
            this.n = new BigInteger(n);
            this.d = BigInteger.Zero;
            CanEncrypt = true;
            CanDecrypt = false;
        }

        // Encrypt (duh)
        public byte[] EncryptString(string message, Encoding encoding = null, CryptoPadding padding = null, bool sign = false) => Encrypt((encoding ?? DEFAULT_ENCODING).GetBytes(message), padding, sign);
        public byte[] Encrypt(byte[] message, CryptoPadding padding = null, bool sign = false)
        {
            // Apply dynamic padding
            message = (padding ?? NO_PADDING).Pad(message);

            // Apply fixed padding
            byte[] b1 = new byte[message.Length + 1];
            Array.Copy(message, b1, message.Length);
            b1[message.Length] = 1;
            message = b1;

            // Represent message as a number
            BigInteger m = new BigInteger(message);

            // Encrypt message
            BigInteger cryptomessage = Support.ModExp(m, sign ? d : e, n);

            // Convert encrypted message back to bytes
            return cryptomessage.ToByteArray();
        }

        // Decrypt (duh)
        public string DecryptString(byte[] message, Encoding encoding = null, CryptoPadding padding = null, bool checkSign = false) => new string((encoding ?? DEFAULT_ENCODING).GetChars(Decrypt(message, padding, checkSign)));
        public byte[] Decrypt(byte[] message, CryptoPadding padding = null, bool checkSign = false)
        {

            // Reinterpret encrypted message as a number
            BigInteger cryptomessage = new BigInteger(message);

            // Reverse encryption
            message = Support.ModExp(cryptomessage, checkSign ? e : d, n).ToByteArray();

            // Remove fixed padding
            byte[] b1 = new byte[message.Length - 1];
            Array.Copy(message, b1, message.Length - 1);
            message = b1;

            // Remove dynamic padding
            message = (padding ?? NO_PADDING).Unpad(message);

            return message;
        }

        // Gives you the public key
        public byte[] GetPubK() => e.ToByteArray();

        // Save this RSA instance to correspondingly named files
        public void Save(string fileNameBase, bool force = false)
        {
            if (force || !File.Exists(fileNameBase + ".e")) File.WriteAllBytes(fileNameBase + ".e", e.ToByteArray());
            if (force || !File.Exists(fileNameBase + ".n")) File.WriteAllBytes(fileNameBase + ".n", n.ToByteArray());
            if (force || !File.Exists(fileNameBase + ".d")) File.WriteAllBytes(fileNameBase + ".d", d.ToByteArray());
        }

        // Serialize (for public key distribution)
        public byte[] Serialize() => Support.SerializeBytes(new byte[][] { e.ToByteArray(), n.ToByteArray() });

        // Deserialize RSA data (for key distribution (but the other end (how many parentheses deep can I go?)))
        public static RSA Deserialize(byte[] function, out int read)
        {
            byte[][] rd = Support.DeserializeBytes(function, 2);
            read = rd[0].Length + rd[1].Length + 8;
            return new RSA(rd[0], rd[1]);
        }

        // Check if the data we want to convert into an RSA-instance will cause a crash if we try to parse it
        public static bool CanDeserialize(IEnumerable<byte> data)
        {
            try
            {
                int size = Support.ReadInt(data, 0), size2;
                if (size >= data.Count() - 8) return false;
                size2 = Support.ReadInt(data, 4 + size);
                if (size2 > data.Count() - size - 8) return false;
                return true;
            }
            catch (Exception) { }
            return false;
        }

        // Safely attempt to load RSA keys from files
        public static RSA TryLoad(string fileNameBase) => TryLoad(fileNameBase + ".e", fileNameBase + ".n", fileNameBase + ".d");
        public static RSA TryLoad(string e_file, string n_file, string d_file)
        {
            try
            {
                return new RSA(e_file, n_file, d_file);
            }
            catch (Exception) { }
            return null;
        }

        public override bool Equals(object obj)
            => obj is RSA && ((RSA)obj).CanDecrypt == CanDecrypt && ((RSA)obj).e.Equals(e) && ((RSA)obj).n.Equals(n) && (!CanDecrypt || ((RSA)obj).d.Equals(d));

        public override int GetHashCode()
        {
            var hashCode = 2073836280;
            hashCode = hashCode * -1521134295 + EqualityComparer<BigInteger>.Default.GetHashCode(e);
            hashCode = hashCode * -1521134295 + EqualityComparer<BigInteger>.Default.GetHashCode(n);
            hashCode = hashCode * -1521134295 + EqualityComparer<BigInteger>.Default.GetHashCode(d);
            hashCode = hashCode * -1521134295 + CanEncrypt.GetHashCode();
            hashCode = hashCode * -1521134295 + CanDecrypt.GetHashCode();
            return hashCode;
        }
    }
}
