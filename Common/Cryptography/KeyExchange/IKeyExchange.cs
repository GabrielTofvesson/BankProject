using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Common.Cryptography.KeyExchange
{
    public interface IKeyExchange
    {
        byte[] GetPublicKey();
        byte[] GetSharedSecret(byte[] pub);
    }
}
