using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tofvesson.Common
{
    public static class BinaryHelpers
    {
        // Swap endianness of a given integer
        public static uint SwapEndian(uint value) => (uint)(((value >> 24) & (255 << 0)) | ((value >> 8) & (255 << 8)) | ((value << 8) & (255 << 16)) | ((value << 24) & (255 << 24)));
        public static ulong SwapEndian(ulong value) =>
            ((value >> 56) & 0xFF)           |
            ((value >> 40) & (0xFFUL << 8))  |
            ((value >> 24) & (0xFFUL << 16)) |
            ((value >> 8)  & (0xFFUL << 24)) |
            ((value << 56) & (0xFFUL << 56)) |
            ((value << 40) & (0xFFUL << 48)) |
            ((value << 24) & (0xFFUL << 40)) |
            ((value << 8)  & (0xFFUL << 32)) ;


        // How many bytes to write
        public static int VarIntSize(dynamic integer) =>
                integer is byte ||
                integer <= 240 ? 1 :
                integer <= 2287 ? 2 :
                integer <= 67823 ? 3 :
                integer <= 16777215 ? 4 :
                integer <= 4294967295 ? 5 :
                integer <= 1099511627775 ? 6 :
                integer <= 281474976710655 ? 7 :
                integer <= 72057594037927935 ? 8 :
                9;

        public static ulong ReadVarInt(IEnumerable<byte> from, int offset)
        {
            ulong header = from.ElementAt(0);
            if (header <= 240) return header;
            if (header <= 248) return 240 + 256 * (header - 241) + from.ElementAt(1);
            if (header == 249) return 2288 + 256UL * from.ElementAt(1) + from.ElementAt(2);
            ulong res = from.ElementAt(1) | ((ulong)from.ElementAt(2) << 8) | ((ulong)from.ElementAt(3) << 16);
            if (header > 250)
            {
                res |= (ulong)from.ElementAt(4) << 24;
                if (header > 251)
                {
                    res |= (ulong)from.ElementAt(5) << 32;
                    if (header > 252)
                    {
                        res |= (ulong)from.ElementAt(6) << 40;
                        if (header > 253)
                        {
                            res |= (ulong)from.ElementAt(7) << 48;
                            if (header > 254) res |= (ulong)from.ElementAt(8) << 56;
                        }
                    }
                }
            }
            return res;
        }

        public static bool TryReadVarInt(IEnumerable<byte> from, int offset, out int result)
        {
            bool b = TryReadVarInt(from, offset, out ulong res);
            result = (int)res;
            return b;
        }
        public static bool TryReadVarInt(IEnumerable<byte> from, int offset, out ulong result)
        {
            try
            {
                result = ReadVarInt(from, offset);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }

        public static void WriteVarInt(byte[] to, int offset, dynamic t)
        {
            if (t is byte)
            {
                to[offset] = (byte)t;
                return;
            }

            if (t <= 240) to[offset] = (byte)t;
            else if (t <= 2287)
            {
                to[offset] = (byte)((t - 240) / 256 + 241);
                to[offset + 1] = (byte)((t - 240) % 256);
            }
            else if (t <= 67823)
            {
                to[offset] = 249;
                to[offset + 1] = (byte)((t - 2288) / 256);
                to[offset + 2] = (byte)((t - 2288) % 256);
            }
            else
            {
                to[offset + 1] = (byte)(t & 0xFF);
                to[offset + 2] = (byte)((t >> 8) & 0xFF);
                to[offset + 3] = (byte)((t >> 16) & 0xFF);
                if (t > 16777215)
                {
                    to[offset + 4] = (byte)((t >> 24) & 0xFF);
                    if (t > 4294967295)
                    {
                        to[offset + 5] = (byte)((t >> 32) & 0xFF);
                        if (t > 1099511627775)
                        {
                            to[offset + 6] = (byte)((t >> 40) & 0xFF);
                            if (t > 281474976710655)
                            {
                                to[offset + 7] = (byte)((t >> 48) & 0xFF);
                                if (t > 72057594037927935)
                                {
                                    to[offset] = 255;
                                    to[offset + 8] = (byte)((t >> 56) & 0xFF);
                                }
                                else to[offset] = 254;
                            }
                            else to[offset] = 253;
                        }
                        else to[offset] = 252;
                    }
                    else to[offset] = 251;
                }
                else to[offset] = 250;
            }
        }
    }
}
