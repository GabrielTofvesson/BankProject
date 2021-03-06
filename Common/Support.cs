﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Tofvesson.Crypto
{

    // Just a ton of support methods to make life easier. Almost aboslutely nothing of notable value here
    // Honestly, just continue on to the next file of whatever, unless you have some unbearable desire to give yourself a headache and be completely disappointed by the end of reading this
    public static class Support
    {
        //   --    Math    --
        public static BigInteger ModExp(BigInteger b, BigInteger e, BigInteger m)
        {
            int count = e.ToByteArray().Length * 8;
            BigInteger result = BigInteger.One;
            b = b % m;
            while (count>0)
            {
                if (e % 2 != 0) result = (result * b) % m;
                b = (b * b) % m;
                e >>= 1;
                --count;
            }
            return result;
        }

        public static BigInteger GenerateRandom(this RandomProvider provider, BigInteger bound)
        {
            byte[] b = bound.ToByteArray();
            if (b.Length == 0) return 0;
            byte b1 = b[b.Length - 1];

            provider.GetBytes(b);
            b[b.Length - 1] %= b1;
            return new BigInteger(b);
        }

        public static long HighestBit(this BigInteger b)
        {
            byte[] b1 = b.ToByteArray();
            for (int i = b1.Length - 1; i >= 0; --i)
                if (b1[i] != 0)
                    for (int j = 7; j >= 0; --j)
                        if ((b1[i] & (1 << j)) != 0)
                            return i * 8 + j;
            return -1;
        }

        public static bool BitAt(this BigInteger b, long idx)
        {
            byte[] b1 = b.ToByteArray();
            return (b1[(int)(idx / 8)] & (1 << ((int)(idx % 8)))) != 0;
        }

        public static BigInteger Abs(this BigInteger b) => b < 0 ? -b : b;

        /// <summary>
        /// Uses the fermat test a given amount of times to test whether or not a supplied interger is probably prime.
        /// </summary>
        /// <param name="b">Value to test primality of</param>
        /// <param name="provider">Random provider used to generate values to test b against</param>
        /// <param name="certainty">How many times the test should be performed. More iterations means higher certainty, but at the cost of performance!</param>
        /// <returns>Whether or not the given value is probably prime or not</returns>
        public static bool IsProbablePrime(BigInteger b, RandomProvider provider, int certainty)
        {
            BigInteger e = b - 1;
            byte[] b1 = b.ToByteArray();
            byte last = b1[b1.Length-1];
            int len = b1.Length - 1;
            for (int i = 0; i < certainty; ++i)
            {
                byte[] gen = new byte[provider.NextInt(len)+1];
                provider.GetBytes(gen);
                if (last != 0 && gen.Length==len+1) gen[gen.Length - 1] %= last;
                else gen[gen.Length - 1] &= 127;
                
                BigInteger test = new BigInteger(gen);
                if (ModExp(test, e, b) != 1) return false;
            }
            return true;
        }

        /// <summary>
        /// Calculate the greatest common divisor for two values.
        /// </summary>
        /// <param name="b1">First value</param>
        /// <param name="b2">Second value</param>
        /// <returns>The greatest common divisor</returns>
        public static BigInteger GCD(BigInteger b1, BigInteger b2)
        {
            BigInteger tmp;
            while ((tmp = b1 % b2) != 0)
            {
                b1 = b2;
                b2 = tmp;
            }
            return b2;
        }

        public static int CollectiveLength(this string[] s)
        {
            int i = 0;
            foreach (var s1 in s) i += s1.Length;
            return i;
        }

        /// <summary>
        /// Linear diophantine equations. Calculates the modular multiplicative inverse for a given value and a given modulus.
        /// For: ax + by = 1
        /// Where 'a' and 'b' are known factors
        /// </summary>
        /// <param name="in1">First known factor (a)</param>
        /// <param name="in2">Second known factor (b)</param>
        /// <returns>A pair of factors that fulfill the aforementioned equations (if possible), where Item1 corresponds to 'x' and Item2 corresponds to 'y'. If the two supplied known factors are not coprime, both factors will be 0</returns>
        public static KeyValuePair<BigInteger, BigInteger> Dio(BigInteger in1, BigInteger in2)
        {
            // Euclidean algorithm
            BigInteger tmp;
            var i1 = in1;
            var i2 = in2;
            if (i1 <= BigInteger.Zero || i2 <= BigInteger.Zero || i1 == i2 || i1 % i2 == BigInteger.Zero || i2 % i1 == BigInteger.Zero)
            {
                return new KeyValuePair<BigInteger, BigInteger>(BigInteger.Zero, BigInteger.Zero);
            }
            var minusOne = new BigInteger(-1);
            var e_m = new BigInteger(-1L);
            var collect = new Stack<BigInteger>();
            while ((e_m = i1 % i2) != BigInteger.Zero)
            {
                collect.Push(i1 / i2 * minusOne);
                i1 = i2;
                i2 = e_m;
            }

            // There are no solutions because 'a' and 'b' are not coprime
            if (i2 != BigInteger.One)
                return new KeyValuePair<BigInteger, BigInteger>(BigInteger.Zero, BigInteger.Zero);


            // Extended euclidean algorithm
            var restrack_first = BigInteger.One;
            var restrack_second = collect.Pop();

            while (collect.Count > 0)
            {
                tmp = restrack_second;
                restrack_second = restrack_first + restrack_second * collect.Pop();
                restrack_first = tmp;
            }
            return new KeyValuePair<BigInteger, BigInteger>(restrack_first, restrack_second);
        }

        /// <summary>
        /// Generate a prime number using with a given approximate length and byte length margin
        /// </summary>
        /// <param name="threads">How many threads to use to generate primes</param>
        /// <param name="approximateByteCount">The byte array length around which the prime generator will select lengths</param>
        /// <param name="byteMargin">Allowed deviation of byte length from approximateByteCount</param>
        /// <param name="certainty">How many iterations of the fermat test should be run to test primailty for each generated number</param>
        /// <param name="provider">Random provider that will be used to generate random primes</param>
        /// <returns>A prime number that is aproximately approximateByteCount long</returns>
        public static BigInteger GeneratePrime(int threads, int approximateByteCount, int byteMargin, int certainty, RandomProvider provider)
        {
            var found = false;
            BigInteger result = BigInteger.Zero;
            for(int i = 0; i<threads; ++i)
                Task.Factory.StartNew(() =>
                {
                    char left = '\0';
                    byte rand = 0;
                    BigInteger b = BigInteger.Zero;
                    while (!found)
                    {
                        if (left == 0)
                        {
                            rand = provider.GetBytes(1)[0];
                            left = (char)8;
                        }

                        byte[] b1 = provider.GetBytes(approximateByteCount + (provider.GetBytes(1)[0] % byteMargin) * (rand % 2 == 1 ? 1 : -1));
                        b1[0] |= 1;  // Always odd
                        b1[b1.Length - 1] &= 127;  // Always positive
                        b = new BigInteger(b1);
                        rand >>= 1;
                        --left;
                        if (IsProbablePrime(b, provider, certainty))
                        {
                            found = true;
                            result = b;
                        }
                    }
                });
            while (!found) System.Threading.Thread.Sleep(125);
            return result;
        }

        public static string ToTruncatedString(this decimal d, int maxdecimals = 3)
        {
            if (maxdecimals < 0) maxdecimals = 0;
            StringBuilder builder = new StringBuilder(d.ToString());
            int decimalIdx = builder.IndexOf('.');
            if (builder.Length - decimalIdx - 1 > maxdecimals) builder.Length = decimalIdx + maxdecimals + 1;
            if (maxdecimals == 0) --builder.Length;
            return builder.ToString();
        }


        //  --    Net    --
        /// <summary>
        /// Finds an IPv4a address in the address list.
        /// </summary>
        /// <param name="entry">IPHostEntry to get the address from</param>
        /// <returns>An IPv4 address if available, otherwise null</returns>
        public static IPAddress GetIPV4(this IPHostEntry entry)
        {
            foreach (IPAddress addr in entry.AddressList)
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr;
            return null;
        }


        //   --    Arrays/Collections    --
        /// <summary>
        /// Pad or truncate this array to the specified length. Padding is performed by filling the new indicies with 0's. Truncation removes bytes from the end.
        /// </summary>
        /// <typeparam name="T">The array type</typeparam>
        /// <param name="t">The array to resize</param>
        /// <param name="length">Target length</param>
        /// <returns>A resized array</returns>
        public static T[] ToLength<T>(this T[] t, int length)
        {
            var t1 = new T[length];
            Array.Copy(t, t1, Math.Min(length, t.Length));
            return t1;
        }

        public static T[] ForEach<T>(this T[] t, Func<T, T> action)
        {
            for (int i = 0; i < t.Length; ++i)
                t[i] = action(t[i]);
            return t;
        }

        // Convert an enumerable object containing strings into a readable format
        public static string ToReadableString(this IEnumerable<string> e)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('[');
            foreach (var entry in e) builder.Append('"').Append(entry.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\", ");
            if (builder.Length != 1) builder.Length -= 2;
            return builder.Append(']').ToString();
        }

        /// <summary>
        /// Reads a serialized 32-bit integer from the byte collection
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static int ReadInt(IEnumerable<byte> data, int offset)
        {
            int result = 0;
            for (int i = 0; i < 4; ++i)
                result |= data.ElementAt(i + offset) << (i * 8);
            return result;
        }

        public static int ArrayContains(byte[] b, byte[] seq, bool fromStart = true)
        {
            int track = 0;
            for (int i = fromStart ? 0 : b.Length - 1; (fromStart && i < b.Length) || (!fromStart && i >= 0); i+=fromStart?1:-1)
                if (b[i] == seq[fromStart?track:seq.Length - 1 - track])
                {
                    if (++track == seq.Length) return i;
                }
                else track = 0;
            return -1;
        }

        public static byte[] WriteToArray(byte[] target, int data, int offset)
        {
            for (int i = 0; i < 4; ++i)
                target[i + offset] = (byte)((data >> (i * 8))&255);
            return target;
        }

        public static byte[] WriteContiguous(byte[] target, int offset, params int[] data)
        {
            for (int i = 0; i < data.Length; ++i) WriteToArray(target, data[i], offset + i * 4);
            return target;
        }

        public static byte[] WriteToArray(byte[] target, uint data, int offset)
        {
            for (int i = 0; i < 4; ++i)
                target[i + offset] = (byte)((data >> (i * 8)) & 255);
            return target;
        }

        public static byte[] WriteContiguous(byte[] target, int offset, params uint[] data)
        {
            for (int i = 0; i < data.Length; ++i) WriteToArray(target, data[i], offset + i * 4);
            return target;
        }

        public static byte[] Concatenate(params byte[][] bytes)
        {
            int alloc = 0;
            foreach (byte[] b in bytes) alloc += b.Length;
            byte[] result = new byte[alloc];
            alloc = 0;
            for(int i = 0; i<bytes.Length; ++i)
            {
                Array.Copy(bytes[i], 0, result, alloc, bytes[i].Length);
                alloc += bytes[i].Length;
            }
            return result;
        }

        public static string ToHexString(this byte[] value, bool bigEndian = true)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = bigEndian ? value.Length - 1 : 0; (bigEndian && i >= 0) || (!bigEndian && i < value.Length); i += bigEndian ? -1 : 1)
            {
                builder.Append((char)((((value[i] >> 4) < 10) ? 48 : 87) + (value[i] >> 4)));
                builder.Append((char)((((value[i] & 15) < 10) ? 48 : 87) + (value[i] & 15)));
            }
            return builder.ToString();
        }

        public static void ArrayCopy<T>(IEnumerable<T> source, int sourceOffset, T[] destination, int offset, int length)
        {
            for (int i = 0; i < length; ++i) destination[i + offset] = source.ElementAt<T>(i+sourceOffset);
        }

        public static string ArrayToString(byte[] array)
        {
            StringBuilder builder = new StringBuilder().Append('[');
            for (int i = 0; i < array.Length; ++i)
            {
                builder.Append(array[i]);
                if (i != array.Length - 1) builder.Append(", ");
            }
            return builder.Append(']').ToString();
        }

        public static bool ArraysEqual<T>(T[] t1, T[] t2)
        {
            if (t1 == t2) return true;
            else if (t1 == null) return false;
            else if (t1.Length != t2.Length) return false;
            for (int i = 0; i < t1.Length; ++i)
                if (!ObjectsEqual(t1[i], t2[i]))
                    return false;
            return true;
        }

        public static bool ObjectsEqual(object o1, object o2) => (o1 == null && o2 == null) || (o1 != null && o1.Equals(o2));

        public static void EnqueueAll<T>(this Queue<T> q, IEnumerable<T> items, int offset, int length)
        {
            for (int i = 0; i < length; ++i) q.Enqueue(items.ElementAt(i+offset));
        }
        public static T[]Dequeue<T>(this Queue<T> q, int count)
        {
            T[] t = new T[count];
            for (int i = 0; i < count; ++i) t[i] = q.Dequeue();
            return t;
        }

        public static byte[] SerializeBytes(byte[][] bytes)
        {
            int collectSize = 0;
            for (int i = 0; i < bytes.Length; ++i) collectSize += bytes[i].Length;
            byte[] output = new byte[collectSize + 4*bytes.Length];
            collectSize = 0;
            for(int i = 0; i<bytes.Length; ++i)
            {
                WriteToArray(output, bytes[i].Length, collectSize);
                Array.Copy(bytes[i], 0, output, collectSize + 4, bytes[i].Length);
                collectSize += bytes[i].Length + 4;
            }
            return output;
        }

        public static byte[][] DeserializeBytes(byte[] message, int messageCount)
        {
            byte[][] output = new byte[messageCount][];
            int offset = 0;
            for(int i = 0; i< messageCount; ++i)
            {
                int size = ReadInt(message, offset);
                if (size > message.Length - offset - 4 || (i!=messageCount-1 && size==message.Length-offset-4))
                    throw new IndexOutOfRangeException("Attempted to read more bytes than are available");
                offset += 4;
                output[i] = new byte[size];
                Array.Copy(message, offset, output[i], 0, size);
                offset += size;
            }
            return output;
        }

        public static T[] SubArray<T>(this T[] array, int start, int end)
        {
            T[] res = new T[end-start];
            for (int i = start; i < end; ++i) res[i - start] = array[i];
            return res;
        }

        public static byte[] XOR(this byte[] array, byte[] xor)
        {
            for (int i = Math.Min(array.Length, xor.Length) - 1; i >= 0; --i) array[i] ^= xor[i];
            return array;
        }

        public static bool EqualsIgnoreCase(this string s, string s1) => s.ToLower().Equals(s1.ToLower());
        public static string ToUTF8String(this byte[] b) => new string(Encoding.UTF8.GetChars(b));
        public static byte[] ToUTF8Bytes(this string s) => Encoding.UTF8.GetBytes(s);


        //  --    Misc    --
        public static int IndexOf(this StringBuilder s, char c)
        {
            for (int i = 0; i < s.Length; ++i)
                if (s[i] == c)
                    return i;
            return -1;
        }

        // Allows deconstruction when iterating over a collection of Tuples
        public static void Deconstruct<T1, T2>(this Tuple<T1, T2> tuple, out T1 key, out T2 value)
        {
            key = tuple.Item1;
            value = tuple.Item2;
        }
        public static XmlNode ContainsNamedNode(string name, XmlNodeList lst)
        {
            for (int i = lst.Count - 1; i >= 0; --i)
                if (lst.Item(i).Name.Equals(name))
                    return lst.Item(i);
            return null;
        }
        public static bool IsNumber(this char c) => c > 47 && c < 58;
        public static bool IsAlphabetical(this char c) => (c > 64 && c < 91) || (c > 96 && c < 123);
        public static bool IsAlphaNumeric(this char c) => c.IsNumber() || c.IsAlphabetical();
        public static bool IsDecimal(this char c) => c == '.' || c.IsNumber();
        
        // Swap endianness of a given integer
        public static uint SwapEndian(uint value) => (uint)(((value >> 24) & (255 << 0)) | ((value >> 8) & (255 << 8)) | ((value << 8) & (255 << 16)) | ((value << 24) & (255 << 24)));
        public static ulong SwapEndian(ulong value) =>
            ((value >> 56) & 0xFF) |
            ((value >> 40) & (0xFFUL << 8)) |
            ((value >> 24) & (0xFFUL << 16)) |
            ((value >> 8) & (0xFFUL << 24)) |
            ((value << 56) & (0xFFUL << 56)) |
            ((value << 40) & (0xFFUL << 48)) |
            ((value << 24) & (0xFFUL << 40)) |
            ((value << 8) & (0xFFUL << 32));

        public static ulong RightShift(this ulong value, int shift) => shift < 0 ? value << -shift : value >> shift;
        public static string ToHexString(byte[] value)
        {
            StringBuilder builder = new StringBuilder();
            foreach(byte b in value)
            {
                builder.Append((char)((((b >> 4) < 10) ? 48 : 87) + (b >> 4)));
                builder.Append((char)((((b & 15) < 10) ? 48 : 87) + (b & 15)));
            }
            return builder.ToString();
        }
        public static string ToBase64String(this string text) => Convert.ToBase64String(text.ToUTF8Bytes());
        public static string FromBase64String(this string text) => Convert.FromBase64String(text).ToUTF8String();

        public static bool ReadYNBool(this TextReader reader, string nonDefault) => reader.ReadLine().ToLower().Equals(nonDefault);

        public static string SerializeStrings(string[] data)
        {
            StringBuilder builder = new StringBuilder();
            foreach (var datum in data) builder.Append(datum.Replace("&", "&amp;").Replace("\n", "&nl;")).Append("&nm;");
            if (builder.Length > 0) builder.Remove(builder.Length - 4, 4);
            return builder.ToString();
        }

        public static string[] DeserializeString(string message)
        {
            List<string> collect = new List<string>();
            const string target = "&nm;";
            int found = 0;
            int prev = 0;
            for(int i = 0; i<message.Length; ++i)
            {
                if (message[i] == target[found])
                {
                    if (++found == target.Length)
                    {
                        collect.Add(message.Substring(prev, (-prev) + (prev = i + 1) - target.Length));
                        found = 0;
                    }

                }
                else found = 0;
            }
            collect.Add(message.Substring(prev));
            string[] data = collect.ToArray();
            for (int i = 0; i < data.Length; ++i) data[i] = data[i].Replace("&nl;", "\n").Replace("&amp;", "&");
            return data;
        }

        public static int Accepts(this ParameterInfo[] info, Type parameterType, int pastFirst = 0)
        {
            int found = -1;
            for(int i = 0; i<info.Length; ++i)
                if (parameterType.IsAssignableFrom(info[i].ParameterType) && ++found >= pastFirst)
                    return i;
            return -1;
        }
    }


    public abstract class RandomProvider
    {
        public abstract byte[] GetBytes(int count);
        public abstract byte[] GetBytes(byte[] buffer);

        // Randomly generates a shortinteger bounded by the supplied integer. If bounding value is <= 0, it will be ignored
        public ushort NextUShort(ushort bound = 0)
        {
            byte[] raw = GetBytes(2);
            ushort result = 0;
            for (byte s = 0; s < 2; ++s)
            {
                result <<= 8;
                result |= raw[s];
            }
            return (ushort)(bound > 0 ? result % bound : result);
        }

        // Randomly generates an integer bounded by the supplied integer. If bounding value is <= 0, it will be ignored
        public uint NextUInt(uint bound = 0)
        {
            byte[] raw = GetBytes(4);
            uint result = 0;
            for (byte s = 0; s < 4; ++s)
            {
                result <<= 8;
                result |= raw[s];
            }
            return bound > 0 ? result % bound : result;
        }

        // Randomly generates a long integer bounded by the supplied integer. If bounding value is <= 0, it will be ignored
        public ulong NextULong(ulong bound = 0)
        {
            byte[] raw = GetBytes(8);
            ulong result = 0;
            for (byte s = 0; s < 8; ++s)
            {
                result <<= 8;
                result |= raw[s];
            }
            return bound > 0 ? result % bound : result;
        }

        public char NextChar(bool alphanumeric = false)
        {
            char c = (char) GetBytes(1)[0];
            if (alphanumeric)
            {
                c %= (char)62;
                c += (char)(c < 10 ? 48 : c < 36 ? 55 : 61);
            }
            return c;
        }

        public string NextString(int length)
        {
            byte[] b = GetBytes(length);
            StringBuilder builder = new StringBuilder(length);
            foreach(var b1 in b)
            {
                char c = (char)(b1%62);
                builder.Append((char)(c+(c < 10 ? 48 : c < 36 ? 55 : 61)));
            }
            return builder.ToString();
        }

        public short NextShort(short bound = 0) => (short)NextUInt((ushort)bound);
        public int NextInt(int bound = 0) => (int)NextUInt((uint)bound);
        public long NextLong(long bound = 0) => (long)NextULong((ulong)bound);
    }

    public sealed class RegularRandomProvider : RandomProvider
    {
        private Random rand;
        public RegularRandomProvider(Random rand) { this.rand = rand; }
        public RegularRandomProvider() : this(new Random(Environment.TickCount)) {}

        // Copy our random reference to the other provider: share a random object
        public void share(RegularRandomProvider provider) => provider.rand = this.rand;

        public override byte[] GetBytes(int count) => GetBytes(new byte[count]);

        public override byte[] GetBytes(byte[] buffer)
        {
            rand.NextBytes(buffer);
            return buffer;
        }
    }

    public sealed class CryptoRandomProvider : RandomProvider
    {
        private RNGCryptoServiceProvider rand;
        public CryptoRandomProvider(RNGCryptoServiceProvider rand) { this.rand = rand; }
        public CryptoRandomProvider() : this(new RNGCryptoServiceProvider()) { }

        // Copy our random reference to the other provider: share a random object
        public void share(CryptoRandomProvider provider) => provider.rand = this.rand;

        public override byte[] GetBytes(int count) => GetBytes(new byte[count]);

        public override byte[] GetBytes(byte[] buffer)
        {
            rand.GetBytes(buffer);
            return buffer;
        }
    }

    public sealed class DummyRandomProvider : RandomProvider
    {
        public override byte[] GetBytes(int count) => new byte[count];

        public override byte[] GetBytes(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; ++i) buffer[i] = 0;
            return buffer;
        }
    }

    public static class RandomSupport
    {
        public static BigInteger GenerateBoundedRandom(BigInteger max, RandomProvider provider)
        {
            byte[] b = max.ToByteArray();
            byte maxLast = b[b.Length - 1];
            provider.GetBytes(b);
            if (maxLast != 0) b[b.Length - 1] %= maxLast;
            b[b.Length - 1] |= 127;
            return new BigInteger(b);
        }
    }
}
