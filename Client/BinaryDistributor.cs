using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public class BinaryDistributor
    {
        private static readonly byte[] holder = new byte[8];
        private static readonly float[] holder_f = new float[1];
        private static readonly double[] holder_d = new double[1];

        private readonly byte[] readFrom;
        private long bitCount = 0;
        public BinaryDistributor(byte[] readFrom) => this.readFrom = readFrom;

        public bool ReadBit()
        {
            bool result = (readFrom[bitCount / 8] & (byte)(1 << (int)(bitCount % 8))) != 0;
            ++bitCount;
            return result;
        }

        public byte ReadByte()
        {
            int shift = (int)(bitCount % 8);
            int index = (int)(bitCount / 8);
            byte lower_mask = (byte)(0xFF << shift);
            byte upper_mask = (byte)~lower_mask;
            byte result = (byte)(((readFrom[index] & lower_mask) >> shift) | (shift == 0 ? 0 : (readFrom[index + 1] & upper_mask) << (8 - shift)));
            bitCount += 8;
            return result;
        }

        public float ReadFloat() => ReadFloating<float>();
        public double ReadDouble() => ReadFloating<double>();
        public float[] ReadFloatArray() => ReadFloatingArray<float>();
        public double[] ReadDoubleArray() => ReadFloatingArray<double>();

        private T[] ReadFloatingArray<T>()
        {
            short size = (short)(ReadByte() | (ReadByte() << 8));
            T[] result = new T[size];
            for (short s = 0; s < size; ++s)
                result[s] = ReadFloating<T>();
            return result;
        }

        private T ReadFloating<T>()
        {
            int size = Marshal.SizeOf(typeof(T));
            Array type_holder = size == 4 ? holder_f as Array: holder_d as Array;
            T result;
            lock(type_holder)
                lock (holder)
                {
                    for (int i = 0; i < size; ++i)
                        holder.SetValue(ReadByte(), i);
                    Buffer.BlockCopy(holder, 0, type_holder, 0, size);
                    result = (T) type_holder.GetValue(0);
                }
            return result;
        }
    }
}
