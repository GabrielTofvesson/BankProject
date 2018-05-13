using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Common;
using Tofvesson.Crypto;

namespace Tofvesson.Net
{
    // Helper methods. WithHeader() should really just be in Support.cs
    public static class NetSupport
    {
        public enum Compression { int16, int32, int64 }
        public static byte[] WithHeader(string message) => WithHeader(Encoding.UTF8.GetBytes(message));
        public static byte[] WithHeader(byte[] message)
        {
            int i = BinaryHelpers.VarIntSize(message.Length);
            byte[] nmsg = new byte[message.Length + i];
            //Support.WriteToArray(nmsg, message.Length, 0);
            BinaryHelpers.WriteVarInt(nmsg, 0, message.Length);
            Array.Copy(message, 0, nmsg, i, message.Length);
            Debug.WriteLine($"Compression:  {nmsg.Length}/{Compress(nmsg, Compression.int16).Length}/{Compress(nmsg, Compression.int32).Length}/{Compress(nmsg, Compression.int64).Length}");
            Debug.WriteLine($"Matches: {Support.ArraysEqual(nmsg, Decompress(Compress(nmsg)))}");
            return nmsg;
        }

        public static byte[] Decompress(byte[] cmpMessage, Compression method = Compression.int32)
        {
            BitReader reader = new BitReader(cmpMessage);
            byte[] decomp = new byte[reader.ReadUInt()];
            int size = method == Compression.int16 ? 2 : method == Compression.int32 ? 4 : 8;
            int count = (decomp.Length / size) + (decomp.Length % size == 0 ? 0 : 1);
            for(int i = 0; i<count; ++i)
            {
                dynamic value = size == 2 ? reader.ReadUShort() : size == 4 ? reader.ReadUInt() : reader.ReadULong();
                for (int j = Math.Min(size, decomp.Length - (i * size)) - 1; j >= 0; --j) decomp[(i * size) + j] = (byte)((int)(value >> (8 * j)) & 0xFF);
            }
            return decomp;
        }

        public static byte[] FromHeaded(byte[] msg, int offset) => msg.SubArray(offset + 4, offset + 4 + Support.ReadInt(msg, offset));

        internal static void DoStateCheck(bool state, bool target)
        {
            if (state != target) throw new InvalidOperationException("Bad state!");
        }

        private delegate void WriteFunc(BitWriter writer, byte[] data, int index);
        private static WriteFunc
            func16 = (w, d, i) => w.WriteUShort(ReadUShort(d, i * 2)),
            func32 = (w, d, i) => w.WriteUInt(ReadUInt(d, i * 4)),
            func64 = (w, d, i) => w.WriteULong(ReadULong(d, i * 8));
        private static byte[] Compress(byte[] data, Compression method = Compression.int32)
        {
            int size = method == Compression.int16 ? 2 : method == Compression.int32 ? 4 : 8;
            int count = (data.Length / size) + (data.Length % size == 0 ? 0 : 1);
            WriteFunc func = size == 2 ? func16 : size == 4 ? func32 : func64;
            using (BitWriter writer = new BitWriter())
            {
                writer.WriteUInt((uint)data.Length);
                for (int i = 0; i < count; ++i)
                    func(writer, data, i);
                return writer.Finalize();
            }
        }

        private static ushort ReadUShort(byte[] b, int offset) =>
            (ushort)((ushort)TryReadByte(b, offset) |
            (ushort)((ushort)TryReadByte(b, offset + 1) << 8));

        private static uint ReadUInt(byte[] b, int offset) =>
             (uint)TryReadByte(b, offset) |
            ((uint)TryReadByte(b, offset + 1) << 8) |
            ((uint)TryReadByte(b, offset + 2) << 16) |
            ((uint)TryReadByte(b, offset + 3) << 24);

        private static ulong ReadULong(byte[] b, int offset) =>
             (ulong)TryReadByte(b, offset) |
            ((ulong)TryReadByte(b, offset + 1) << 8) |
            ((ulong)TryReadByte(b, offset + 2) << 16) |
            ((ulong)TryReadByte(b, offset + 3) << 24) |
            ((ulong)TryReadByte(b, offset + 4) << 32) |
            ((ulong)TryReadByte(b, offset + 5) << 40) |
            ((ulong)TryReadByte(b, offset + 6) << 48) |
            ((ulong)TryReadByte(b, offset + 7) << 56);

        private static byte TryReadByte(byte[] b, int idx) => idx >= b.Length ? (byte) 0 : b[idx];
    }
}
