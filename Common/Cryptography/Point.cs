using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Common.Cryptography
{
    public class Point
    {
        public static readonly Point POINT_AT_INFINITY = new Point();
        public BigInteger X { get; private set; }
        public BigInteger Y { get; private set; }
        private bool pai = false;
        public Point(BigInteger x, BigInteger y)
        {
            X = x;
            Y = y;
        }
        private Point() { pai = true; } // Accessing corrdinates causes undocumented behaviour
        public override string ToString()
        {
            return pai ? "(POINT_AT_INFINITY)" : "(" + X + ", " + Y + ")";
        }
    }
}
