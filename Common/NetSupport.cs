using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Common
{
    // Helper methods. WithHeader() should really just be in Support.cs
    public static class NetSupport
    {
        public static byte[] WithHeader(string message) => WithHeader(Encoding.UTF8.GetBytes(message));
        public static byte[] WithHeader(byte[] message)
        {
            byte[] nmsg = new byte[message.Length + 4];
            Support.WriteToArray(nmsg, message.Length, 0);
            Array.Copy(message, 0, nmsg, 4, message.Length);
            return nmsg;
        }

        public static byte[] FromHeaded(byte[] msg, int offset) => msg.SubArray(offset + 4, offset + 4 + Support.ReadInt(msg, offset));

        internal static void DoStateCheck(bool state, bool target)
        {
            if (state != target) throw new InvalidOperationException("Bad state!");
        }
    }
}
