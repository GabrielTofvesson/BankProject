﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Tofvesson.Common
{
    public sealed class BinaryCollector : IDisposable
    {
        // Collects reusable 
        private static readonly List<WeakReference<object[]>> expired = new List<WeakReference<object[]>>();

        private static readonly byte[] holder = new byte[8];
        private static readonly float[] holder_f = new float[1];
        private static readonly double[] holder_d = new double[1];
        private static readonly ulong[] holder_u = new ulong[1];
        private static readonly uint[] holder_i = new uint[1];
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
            bool size = false;
            if (type.IsArray)
            {
                var array = t as Array;
                Serialize((ushort)array.Length, writeTo, ref bitOffset);
                foreach (var element in array)
                    Serialize(element, writeTo, ref bitOffset);
            }
            else if (IsSupportedType(type))
            {
                long offset = GetBitAllocation(type);
                if (type == typeof(bool))
                {
                    WriteBit(writeTo, t as bool? ?? false, bitOffset);
                    bitOffset += offset;
                }
                else if (type == typeof(decimal))
                {
                    WriteDynamic(writeTo, dec_lo.GetValue(t), 4, bitOffset);
                    WriteDynamic(writeTo, dec_mid.GetValue(t), 4, bitOffset + 32);
                    WriteDynamic(writeTo, dec_hi.GetValue(t), 4, bitOffset + 64);
                    WriteDynamic(writeTo, dec_flags.GetValue(t), 4, bitOffset + 96);
                    bitOffset += offset;
                }
                else if ((size = type == typeof(float)) || type == typeof(double))
                {
                    int bytes = size ? 4 : 8;
                    Array type_holder = size ? holder_f as Array : holder_d as Array; // Fetch the preallocated array
                    Array result_holder = size ? holder_i as Array : holder_u as Array;
                    lock (result_holder)
                        lock (type_holder)
                        {
                            // Clear artifacts
                            if (size) result_holder.SetValue(0U, 0);
                            else result_holder.SetValue(0UL, 0);
                            type_holder.SetValue(t, 0); // Insert the value to convert into the preallocated holder array
                            Buffer.BlockCopy(type_holder, 0, result_holder, 0, bytes); // Perform an internal copy to the byte-based holder
                            dynamic d = result_holder.GetValue(0);

                            // Since floating point flag bits are seemingly the highest bytes of the floating point values
                            // and even very small values have them, we swap the endianness in the hopes of reducing the size
                            Serialize(Support.SwapEndian(d), writeTo, ref bitOffset);
                        }
                    //bitOffset += offset;
                }
                else
                {
                    bool signed = IsSigned(t.GetType());
                    dynamic value;
                    if (signed)
                    {
                        Type t1 = t.GetType();
                        if (t1 == typeof(sbyte)) value = (byte)ZigZagEncode(t);
                        else if (t1 == typeof(short)) value = (ushort)ZigZagEncode(t);
                        else if (t1 == typeof(int)) value = (uint)ZigZagEncode(t);
                        else /*if (t1 == typeof(long))*/ value = (ulong)ZigZagEncode(t);
                    }
                    else value = t;

                    if (value <= 240) WriteByte(writeTo, value, bitOffset);
                    else if (value <= 2287)
                    {
                        WriteByte(writeTo, (value - 240) / 256 + 241, bitOffset);
                        WriteByte(writeTo, (value - 240) % 256, bitOffset + 8);
                    }
                    else if (value <= 67823)
                    {
                        WriteByte(writeTo, 249, bitOffset);
                        WriteByte(writeTo, (value - 2288) / 256, bitOffset + 8);
                        WriteByte(writeTo, (value - 2288) % 256, bitOffset + 16);
                    }
                    else
                    {
                        WriteByte(writeTo, value & 255, bitOffset + 8);
                        WriteByte(writeTo, (value >> 8) & 255, bitOffset + 16);
                        WriteByte(writeTo, (value >> 16) & 255, bitOffset + 24);
                        if (value > 16777215)
                        {
                            WriteByte(writeTo, (value >> 24) & 255, bitOffset + 32);
                            if (value > 4294967295)
                            {
                                WriteByte(writeTo, (value >> 32) & 255, bitOffset + 40);
                                if (value > 1099511627775)
                                {
                                    WriteByte(writeTo, (value >> 40) & 55, bitOffset + 48);
                                    if (value > 281474976710655)
                                    {
                                        WriteByte(writeTo, (value >> 48) & 255, bitOffset + 56);
                                        if (value > 72057594037927935)
                                        {
                                            WriteByte(writeTo, 255, bitOffset);
                                            WriteByte(writeTo, (value >> 56) & 255, bitOffset + 64);
                                        }
                                        else WriteByte(writeTo, 254, bitOffset);
                                    }
                                    else WriteByte(writeTo, 253, bitOffset);
                                }
                                else WriteByte(writeTo, 252, bitOffset);
                            }
                            else WriteByte(writeTo, 251, bitOffset);
                        }
                        else WriteByte(writeTo, 250, bitOffset);
                    }
                    bitOffset += BytesToRead(value) * 8;
                }
            }
        }

        private static byte Read7BitRange(byte higher, byte lower, int bottomBits) => (byte)((higher << bottomBits) & (lower & (0xFF << (8-bottomBits))));
        private static byte ReadNBits(byte from, int offset, int count) => (byte)(from & ((0xFF >> (8-count)) << offset));

        private static bool IsSigned(Type t) => Convert.ToBoolean(t.GetField("MinValue").GetValue(null));

        private static Type GetUnsignedType(Type t) =>
            t == typeof(sbyte) ? typeof(byte) :
            t == typeof(short) ? typeof(ushort) :
            t == typeof(int) ? typeof(uint) :
            t == typeof(long) ? typeof(ulong) :
            null;

        private static dynamic ZigZagEncode(dynamic d) => (((d >> (int)(Marshal.SizeOf(d) * 8 - 1))&1) | (d << 1));

        private static long GetBitCount<T>(T t)
        {
            Type type = t.GetType();
            long count = 0;
            if (type.IsArray)
            {
                Type elementType = type.GetElementType();

                count += 16; // Int16 array size. Arrays shouldn't be syncing more than 65k elements
                foreach (var element in t as Array)
                    count += GetBitCount(element);
            }
            else if (IsSupportedType(type))
            {
                long ba = GetBitAllocation(type);
                if (ba == 0) count += Encoding.UTF8.GetByteCount(t as string);
                else if (t is bool || t is decimal) count += ba;
                else count += BytesToRead(t) * 8;
            }
            //else
            //    Debug.LogWarning("MLAPI: The type \"" + b.GetType() + "\" is not supported by the Binary Serializer. It will be ignored");
            return count;
        }

        private static void WriteBit(byte[] b, bool bit, long index)
            => b[index / 8] = (byte)((b[index / 8] & ~(1 << (int)(index % 8))) | (bit ? 1 << (int)(index % 8) : 0));
        private static void WriteByte(byte[] b, dynamic value, long index) => WriteByte(b, (byte)value, index);
        private static void WriteByte(byte[] b, byte value, long index)
        {
            int byteIndex = (int)(index / 8);
            int shift = (int)(index % 8);
            byte upper_mask = (byte)(0xFF << shift);
            byte lower_mask = (byte)~upper_mask;

            b[byteIndex] = (byte)((b[byteIndex] & lower_mask) | (value << shift));
            if(shift != 0 && byteIndex + 1 < b.Length)
                b[byteIndex + 1] = (byte)((b[byteIndex + 1] & upper_mask) | (value >> (8 - shift)));
        }
        private static void WriteBits(byte[] b, byte value, int bits, int offset, long index)
        {
            for (int i = 0; i < bits; ++i)
                WriteBit(b, (value & (1 << (i + offset))) != 0, index + i);
        }
        private static void WriteDynamic(byte[] b, dynamic value, int byteCount, long index)
        {
            for (int i = 0; i < byteCount; ++i)
                WriteByte(b, (byte)((value >> (8 * i)) & 0xFF), index + (8 * i));
        }

        private static int BytesToRead(dynamic integer)
        {
            bool size;
            if((size=integer is float) || integer is double)
            {
                int bytes = size ? 4 : 8;
                Array type_holder = size ? holder_f as Array : holder_d as Array; // Fetch the preallocated array
                Array result_holder = size ? holder_i as Array : holder_u as Array;
                lock (result_holder)
                    lock (type_holder)
                    {
                        // Clear artifacts
                        if (size) result_holder.SetValue(0U, 0);
                        else result_holder.SetValue(0UL, 0);

                        type_holder.SetValue(integer, 0); // Insert the value to convert into the preallocated holder array
                        Buffer.BlockCopy(type_holder, 0, result_holder, 0, bytes); // Perform an internal copy to the byte-based holder
                        integer = Support.SwapEndian(integer = result_holder.GetValue(0));
                    }
            }
            return
                integer <= 240 ? 1 :
                integer <= 2287 ? 2 :
                integer <= 67823 ? 3 :
                integer <= 16777215 ? 4 :
                integer <= 4294967295 ? 5 :
                integer <= 1099511627775 ? 6 :
                integer <= 281474976710655 ? 7 :
                integer <= 72057594037927935 ? 8 :
                9;
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
