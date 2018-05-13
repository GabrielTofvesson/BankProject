﻿#define UNSAFE_PUSH

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Tofvesson.Common
{
    public sealed class BitWriter : IDisposable
    {
        private const int PREALLOC_COLLECT = 10;
        private static readonly Queue<List<object>> listPool = new Queue<List<object>>();
        
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

        static BitWriter()
        {
            dec_lo = typeof(decimal).GetField("lo", BindingFlags.NonPublic);
            dec_mid = typeof(decimal).GetField("mid", BindingFlags.NonPublic);
            dec_hi = typeof(decimal).GetField("hi", BindingFlags.NonPublic);
            dec_flags = typeof(decimal).GetField("flags", BindingFlags.NonPublic);

            for (int i = 0; i < PREALLOC_COLLECT; i++)
            {
                listPool.Enqueue(new List<object>());
            }
        }

        private List<object> collect = null;
        private bool tempAlloc = false;

        /// <summary>
        /// Allocates a new binary collector.
        /// </summary>
        public BitWriter()
        {
            if (listPool.Count == 0)
            {
                Debug.WriteLine("BitWriter: Optimized for "+ PREALLOC_COLLECT + " BitWriters. Have you forgotten to dispose?");
                collect = new List<object>();
                tempAlloc = true;
            }
            else
            {
                collect = listPool.Dequeue();
            }
        }

#if UNSAFE_PUSH
        public
#else
        private
# endif
        void Push<T>(T b, bool known = false)
        {
            if (b == null) collect.Add(b);
            else if (b is string || b.GetType().IsArray || IsSupportedType(b.GetType()))
                collect.Add(b is string ? Encoding.UTF8.GetBytes(b as string) : b as object);
            else
                Debug.WriteLine("BitWriter: The type \"" + b.GetType() + "\" is not supported by the Binary Serializer. It will be ignored!");
        }

        // Public write methods to prevent errors when chaning types
        public void WriteBool(bool b)               => Push(b);
        public void WriteFloat(float f)             => Push(f);
        public void WriteDouble(double d)           => Push(d);
        public void WriteByte(byte b)               => Push(b);
        public void WriteUShort(ushort s)           => Push(s);
        public void WriteUInt(uint i)               => Push(i);
        public void WriteULong(ulong l)             => Push(l);
        public void WriteSByte(sbyte b)             => Push(ZigZagEncode(b, 8));
        public void WriteShort(short s)             => Push(ZigZagEncode(s, 8));
        public void WriteInt(int i)                 => Push(ZigZagEncode(i, 8));
        public void WriteLong(long l)               => Push(ZigZagEncode(l, 8));
        public void WriteString(string s)           => Push(s);
        public void WriteAlignBits()                => Push<object>(null);
        public void WriteFloatArray(float[] f, bool known = false)      => PushArray(f, known);
        public void WriteDoubleArray(double[] d, bool known = false)    => PushArray(d, known);
        public void WriteByteArray(byte[] b, bool known = false)        => PushArray(b, known);
        public void WriteUShortArray(ushort[] s, bool known = false)    => PushArray(s, known);
        public void WriteUIntArray(uint[] i, bool known = false)        => PushArray(i, known);
        public void WriteULongArray(ulong[] l, bool known = false)      => PushArray(l, known);
        public void WriteSByteArray(sbyte[] b, bool known = false)      => PushArray(b, known);
        public void WriteShortArray(short[] s, bool known = false)      => PushArray(s, known);
        public void WriteIntArray(int[] i, bool known = false)          => PushArray(i, known);
        public void WriteLongArray(long[] l, bool known = false)        => PushArray(l, known);

#if UNSAFE_PUSH
        public
#else
        private
#endif
        void PushArray<T>(T[] t, bool knownSize = false)
        {
            if (!knownSize) Push((uint)t.Length);
            bool signed = IsSigned(t.GetType().GetElementType());
            int size = Marshal.SizeOf(t.GetType().GetElementType());
            foreach (T t1 in t) Push(signed ? (object)ZigZagEncode(t1 as long? ?? t1 as int? ?? t1 as short? ?? t1 as sbyte? ?? 0, size) : (object)t1);
        }

        // Actually serialize
        public byte[] Finalize()
        {
            byte[] b = new byte[GetFinalizeSize()];
            Finalize(ref b);
            return b;
        }
        public long Finalize(ref byte[] buffer)
        {
            if(buffer == null)
            {
                Debug.WriteLine("BitWriter: no buffer provided");
                return 0;
            }
            long bitCount = 0;
            for (int i = 0; i < collect.Count; ++i) bitCount += collect[i] == null ? (8 - (bitCount % 8)) % 8 : GetBitCount(collect[i]);

            if (buffer.Length < ((bitCount / 8) + (bitCount % 8 == 0 ? 0 : 1)))
            {
                Debug.WriteLine("BitWriter: The buffer size is not large enough");
                return 0;
            }
            long bitOffset = 0;
            bool isAligned = true;
            foreach (var item in collect)
                if (item == null)
                {
                    bitOffset += (8 - (bitOffset % 8)) % 8;
                    isAligned = true;
                }
                else Serialize(item, buffer, ref bitOffset, ref isAligned);

            collect.Clear(); // Allow GC to clean up any dangling references
            return (bitCount / 8) + (bitCount % 8 == 0 ? 0 : 1);
        }

        // Compute output size (in bytes)
        public long GetFinalizeSize()
        {
            long bitCount = 0;
            for (int i = 0; i < collect.Count; ++i) bitCount += collect[i]==null ? (8 - (bitCount % 8)) % 8 : GetBitCount(collect[i]);
            return ((bitCount / 8) + (bitCount % 8 == 0 ? 0 : 1));
        }

        // Serialization: Originally used dynamic, but due to the requirement of an adaptation using without dynamic,
        // it has been completely retrofitted to not use dynamic
        private static void Serialize<T>(T t, byte[] writeTo, ref long bitOffset, ref bool isAligned)
        {
            Type type = t.GetType();
            bool size = false;
            if (type.IsArray)
            {
                var array = t as Array;
                Serialize((uint)array.Length, writeTo, ref bitOffset, ref isAligned);
                foreach (var element in array)
                    Serialize(element, writeTo, ref bitOffset, ref isAligned);
            }
            else if (IsSupportedType(type))
            {
                long offset = t is bool ? 1 : BytesToRead(t) * 8;
                if (type == typeof(bool))
                {
                    WriteBit(writeTo, t as bool? ?? false, bitOffset);
                    bitOffset += offset;
                    isAligned = bitOffset % 8 == 0;
                }
                else if (type == typeof(decimal))
                {
                    WriteDynamic(writeTo, (int)dec_lo.GetValue(t), 4, bitOffset, isAligned);
                    WriteDynamic(writeTo, (int)dec_mid.GetValue(t), 4, bitOffset + 32, isAligned);
                    WriteDynamic(writeTo, (int)dec_hi.GetValue(t), 4, bitOffset + 64, isAligned);
                    WriteDynamic(writeTo, (int)dec_flags.GetValue(t), 4, bitOffset + 96, isAligned);
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

                            // Since floating point flag bits are seemingly the highest bytes of the floating point values
                            // and even very small values have them, we swap the endianness in the hopes of reducing the size
                            if(size) Serialize(BinaryHelpers.SwapEndian((uint)result_holder.GetValue(0)), writeTo, ref bitOffset, ref isAligned);
                            else Serialize(BinaryHelpers.SwapEndian((ulong)result_holder.GetValue(0)), writeTo, ref bitOffset, ref isAligned);
                        }
                }
                else
                {
                    //bool signed = IsSigned(t.GetType());
                    ulong value;
                    /*if (signed)
                    {
                        Type t1 = t.GetType();
                        if (t1 == typeof(sbyte)) value = (byte)ZigZagEncode(t as sbyte? ?? 0, 1);
                        else if (t1 == typeof(short)) value = (ushort)ZigZagEncode(t as short? ?? 0, 2);
                        else if (t1 == typeof(int)) value = (uint)ZigZagEncode(t as int? ?? 0, 4);
                        else /*if (t1 == typeof(long)) value = (ulong)ZigZagEncode(t as long? ?? 0, 8);
                    }
                    else*/
                    if (t is byte)
                    {
                        WriteByte(writeTo, t as byte? ?? 0, bitOffset, isAligned);
                        bitOffset += 8;
                        return;
                    }
                    else if (t is ushort) value = t as ushort? ?? 0;
                    else if (t is uint) value = t as uint? ?? 0;
                    else /*if (t is ulong)*/ value = t as ulong? ?? 0;

                    // VarInt implementation
                    if (value <= 240) WriteByte(writeTo, (byte)value, bitOffset, isAligned);
                    else if (value <= 2287)
                    {
                        WriteByte(writeTo, (value - 240) / 256 + 241, bitOffset, isAligned);
                        WriteByte(writeTo, (value - 240) % 256, bitOffset + 8, isAligned);
                    }
                    else if (value <= 67823)
                    {
                        WriteByte(writeTo, 249, bitOffset, isAligned);
                        WriteByte(writeTo, (value - 2288) / 256, bitOffset + 8, isAligned);
                        WriteByte(writeTo, (value - 2288) % 256, bitOffset + 16, isAligned);
                    }
                    else
                    {
                        WriteByte(writeTo, value & 255, bitOffset + 8, isAligned);
                        WriteByte(writeTo, (value >> 8) & 255, bitOffset + 16, isAligned);
                        WriteByte(writeTo, (value >> 16) & 255, bitOffset + 24, isAligned);
                        if (value > 16777215)
                        {
                            WriteByte(writeTo, (value >> 24) & 255, bitOffset + 32, isAligned);
                            if (value > 4294967295)
                            {
                                WriteByte(writeTo, (value >> 32) & 255, bitOffset + 40, isAligned);
                                if (value > 1099511627775)
                                {
                                    WriteByte(writeTo, (value >> 40) & 55, bitOffset + 48, isAligned);
                                    if (value > 281474976710655)
                                    {
                                        WriteByte(writeTo, (value >> 48) & 255, bitOffset + 56, isAligned);
                                        if (value > 72057594037927935)
                                        {
                                            WriteByte(writeTo, 255, bitOffset, isAligned);
                                            WriteByte(writeTo, (value >> 56) & 255, bitOffset + 64, isAligned);
                                        }
                                        else WriteByte(writeTo, 254, bitOffset, isAligned);
                                    }
                                    else WriteByte(writeTo, 253, bitOffset, isAligned);
                                }
                                else WriteByte(writeTo, 252, bitOffset, isAligned);
                            }
                            else WriteByte(writeTo, 251, bitOffset, isAligned);
                        }
                        else WriteByte(writeTo, 250, bitOffset, isAligned);
                    }
                    bitOffset += BytesToRead(value) * 8;
                }
            }
        }
        
        // Write oddly bounded data
        private static byte Read7BitRange(byte higher, byte lower, int bottomBits) => (byte)((higher << bottomBits) & (lower & (0xFF << (8-bottomBits))));
        private static byte ReadNBits(byte from, int offset, int count) => (byte)(from & ((0xFF >> (8-count)) << offset));

        // Check if type is signed (int, long, etc)
        private static bool IsSigned(Type t) => t == typeof(sbyte) || t == typeof(short) || t == typeof(int) || t == typeof(long);

        private static Type GetUnsignedType(Type t) =>
            t == typeof(sbyte) ? typeof(byte) :
            t == typeof(short) ? typeof(ushort) :
            t == typeof(int) ? typeof(uint) :
            t == typeof(long) ? typeof(ulong) :
            null;

        // Encode signed values in a way that preserves magnitude
        private static ulong ZigZagEncode(long d, int bytes) => (ulong)(((d >> (bytes * 8 - 1))&1) | (d << 1));

        // Gets the amount of bits required to serialize a given value
        private static long GetBitCount<T>(T t)
        {
            Type type = t.GetType();
            long count = 0;
            if (type.IsArray)
            {
                Type elementType = type.GetElementType();

                count += BytesToRead((t as Array).Length) * 8; // Int16 array size. Arrays shouldn't be syncing more than 65k elements

                if (elementType == typeof(bool)) count += (t as Array).Length;
                else
                    foreach (var element in t as Array)
                        count += GetBitCount(element);
            }
            else if (IsSupportedType(type))
            {
                long ba = t is bool ? 1 : BytesToRead(t)*8;
                if (ba == 0) count += Encoding.UTF8.GetByteCount(t as string);
                else if (t is bool || t is decimal) count += ba;
                else count += BytesToRead(t) * 8;
            }
            //else
            //    Debug.LogWarning("MLAPI: The type \"" + b.GetType() + "\" is not supported by the Binary Serializer. It will be ignored");
            return count;
        }

        // Write methods
        private static void WriteBit(byte[] b, bool bit, long index)
            => b[index / 8] = (byte)((b[index / 8] & ~(1 << (int)(index % 8))) | (bit ? 1 << (int)(index % 8) : 0));
        private static void WriteByte(byte[] b, ulong value, long index, bool isAligned) => WriteByte(b, (byte)value, index, isAligned);
        private static void WriteByte(byte[] b, byte value, long index, bool isAligned)
        {
            if (isAligned) b[index / 8] = value;
            else
            {
                int byteIndex = (int)(index / 8);
                int shift = (int)(index % 8);
                byte upper_mask = (byte)(0xFF << shift);

                b[byteIndex] = (byte)((b[byteIndex] & (byte)~upper_mask) | (value << shift));
                b[byteIndex + 1] = (byte)((b[byteIndex + 1] & upper_mask) | (value >> (8 - shift)));
            }
        }
        private static void WriteDynamic(byte[] b, int value, int byteCount, long index, bool isAligned)
        {
            for (int i = 0; i < byteCount; ++i)
                WriteByte(b, (byte)((value >> (8 * i)) & 0xFF), index + (8 * i), isAligned);
        }

        private static int BytesToRead(object i)
        {
            if (i is byte) return 1;
            bool size;
            ulong integer;
            if (i is decimal) return BytesToRead((int)dec_flags.GetValue(i)) + BytesToRead((int)dec_lo.GetValue(i)) + BytesToRead((int)dec_mid.GetValue(i)) + BytesToRead((int)dec_hi.GetValue(i));
            if ((size = i is float) || i is double)
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

                        type_holder.SetValue(i, 0); // Insert the value to convert into the preallocated holder array
                        Buffer.BlockCopy(type_holder, 0, result_holder, 0, bytes); // Perform an internal copy to the byte-based holder
                        if(size) integer = BinaryHelpers.SwapEndian((uint)result_holder.GetValue(0));
                        else integer = BinaryHelpers.SwapEndian((ulong)result_holder.GetValue(0));
                    }
            }
            else integer = i as ulong? ?? i as uint? ?? i as ushort? ?? i as byte? ?? 0;
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
        
        // Creates a weak reference to the allocated collector so that reuse may be possible
        public void Dispose()
        {
            if (!tempAlloc)
            {
                collect.Clear();
                listPool.Enqueue(collect);
            }
            collect = null; //GC picks this
        }
    }
}
