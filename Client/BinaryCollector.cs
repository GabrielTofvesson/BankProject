using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public sealed class BinaryCollector : IDisposable
    {
        // Collects reusable 
        private static readonly List<WeakReference<object[]>> expired = new List<WeakReference<object[]>>();

        private static readonly List<Type> supportedTypes = new List<Type>()
        {
            typeof(bool),
            typeof(byte),
            typeof(sbyte),
            typeof(char),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(decimal)
        };

        private static readonly FieldInfo
            dec_lo,
            dec_mid,
            dec_hi, 
            dec_flags;

        static BinaryCollector()
        {
            dec_lo = typeof(decimal).GetField("lo", BindingFlags.NonPublic);
            dec_mid = typeof(decimal).GetField("mid", BindingFlags.NonPublic);
            dec_hi = typeof(decimal).GetField("hi", BindingFlags.NonPublic);
            dec_flags = typeof(decimal).GetField("flags", BindingFlags.NonPublic);
        }

        private object[] collect;
        private readonly int bufferSize;
        private int collectCount = 0;

        /// <summary>
        /// Allocates a new binary collector.
        /// </summary>
        public BinaryCollector(int bufferSize)
        {
            this.bufferSize = bufferSize;
            for (int i = expired.Count - 1; i >= 0; --i)
                if (expired[i].TryGetTarget(out collect))
                {
                    if (collect.Length >= bufferSize)
                    {
                        expired.RemoveAt(i); // This entry he been un-expired for now
                        break;
                    }
                }
                else expired.RemoveAt(i); // Entry has been collected by GC
            if (collect == null || collect.Length < bufferSize)
                collect = new object[bufferSize];
        }

        public void Push<T>(T b)
        {
            if (b is string || b.GetType().IsArray || IsSupportedType(b.GetType()))
                collect[collectCount++] = b is string ? Encoding.UTF8.GetBytes(b as string) : b as object;
            //else
            //    Debug.LogWarning("MLAPI: The type \"" + b.GetType() + "\" is not supported by the Binary Serializer. It will be ignored");
        }

        public byte[] ToArray()
        {
            long bitCount = 0;
            for (int i = 0; i < collectCount; ++i) bitCount += GetBitCount(collect[i]);

            byte[] alloc = new byte[(bitCount / 8) + (bitCount % 8 == 0 ? 0 : 1)];
            long bitOffset = 0;
            foreach (var item in collect)
                Serialize(item, alloc, ref bitOffset);

            return alloc;
        }

        private static void Serialize<T>(T t, byte[] writeTo, ref long bitOffset)
        {
            Type type = t.GetType();
            if (type.IsArray)
            {
                var array = t as Array;
                Serialize(array.Length, writeTo, ref bitOffset);
                foreach (var element in array)
                    Serialize(element, writeTo, ref bitOffset);
            }
            else if (IsSupportedType(type))
            {
                long offset = GetBitAllocation(type);
                if (type == typeof(bool)) WriteBit(writeTo, t as bool? ?? false, bitOffset);
                else if(type == typeof(decimal))
                {
                    WriteDynamic(writeTo, dec_lo.GetValue(t), 4, bitOffset);
                    WriteDynamic(writeTo, dec_mid.GetValue(t), 4, bitOffset + 32);
                    WriteDynamic(writeTo, dec_hi.GetValue(t), 4, bitOffset + 64);
                    WriteDynamic(writeTo, dec_flags.GetValue(t), 4, bitOffset + 96);
                }
                else if(type == typeof(float))
                {

                }
                bitOffset += offset;
            }
        }

        private static long GetBitCount<T>(T t)
        {
            Type type = t.GetType();
            long count = 0;
            if (type.IsArray)
            {
                Type elementType = type.GetElementType();
                long allocSize = GetBitAllocation(elementType);
                var array = t as Array;

                count += 2; // Int16 array size. Arrays shouldn't be syncing more than 65k elements

                if (allocSize != 0) // The array contents is known: compute the data size
                    count += allocSize * array.Length;
                else // Unknown array contents type: iteratively assess serialization size
                    foreach (var element in t as Array)
                        count += GetBitCount(element);
            }
            else if(IsSupportedType(type)) count += GetBitAllocation(type);
            //else
            //    Debug.LogWarning("MLAPI: The type \"" + b.GetType() + "\" is not supported by the Binary Serializer. It will be ignored");
            return count;
        }

        private static void WriteBit(byte[] b, bool bit, long index)
            => b[index / 8] = (byte)((b[index / 8] & (1 << (int)(index % 8))) | (bit ? 1 << (int)(index % 8) : 0));
        private static void WriteByte(byte[] b, byte value, long index)
        {
            int byteIndex = (int)(index / 8);
            int shift = (int)(index % 8);
            byte upper_mask = (byte)(0xFF << shift);
            byte lower_mask = (byte)~upper_mask;

            b[byteIndex] = (byte)((b[byteIndex] & lower_mask) | (value << shift));
            if(shift != 0 && byteIndex + 1 < b.Length)
                b[byteIndex + 1] = (byte)((b[byteIndex + 1] & upper_mask) | (value << (8 - shift)));
        }
        private static void WriteDynamic(byte[] b, dynamic value, int byteCount, long index)
        {
            for (int i = 0; i < byteCount; ++i)
                WriteByte(b, (byte)((value >> (8 * i)) & 0xFF), index + (8 * i));
        }

        // Supported datatypes for serialization
        private static bool IsSupportedType(Type t) => supportedTypes.Contains(t);

        // Specifies how many bits will be written
        private static long GetBitAllocation(Type t) =>
            t == typeof(bool) ? 1 :
            t == typeof(byte) ? 8 :
            t == typeof(sbyte) ? 8 :
            t == typeof(short) ? 16 :
            t == typeof(char) ? 16 :
            t == typeof(ushort) ? 16 :
            t == typeof(int) ? 32 :
            t == typeof(uint) ? 32 :
            t == typeof(long) ? 64 :
            t == typeof(ulong) ? 64 :
            t == typeof(float) ? 32 :
            t == typeof(double) ? 64 :
            t == typeof(decimal) ? 128 :
            0; // Unknown type

        // Creates a weak reference to the allocated collector so that reuse may be possible
        public void Dispose()
        {
            expired.Add(new WeakReference<object[]>(collect));
            collect = null;
        }
    }
}
