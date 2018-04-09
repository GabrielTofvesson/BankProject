﻿//#define SAFE_MATH

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Common.Cryptography
{
    public class EllipticCurve
    {
        public enum CurveType { Weierstrass, Montgomery }

        protected readonly BigInteger a, b, modulo;
        protected readonly CurveType type;

        public EllipticCurve(BigInteger a, BigInteger b, BigInteger modulo, CurveType type = CurveType.Weierstrass)
        {
            if (
                (type == CurveType.Weierstrass && (4 * a * a * a) + (27 * b * b) == 0) || // Unfavourable Weierstrass curves
                (type == CurveType.Montgomery && b * (a * a - 4) == 0)                      // Unfavourable Montgomery curves
                ) throw new Exception("Unfavourable curve");
            this.a = a;
            this.b = b;
            this.modulo = modulo;
            this.type = type;
        }

        public Point Add(Point p1, Point p2)
        {
#if SAFE_MATH
            CheckOnCurve(p1);
            CheckOnCurve(p2);
#endif

            // Special cases
            if (p1 == Point.POINT_AT_INFINITY && p2 == Point.POINT_AT_INFINITY) return Point.POINT_AT_INFINITY;
            else if (p1 == Point.POINT_AT_INFINITY) return p2;
            else if (p2 == Point.POINT_AT_INFINITY) return p1;
            else if (p1.X == p2.X && p1.Y == Inverse(p2).Y) return Point.POINT_AT_INFINITY;

            BigInteger x3 = 0, y3 = 0;
            if (type == CurveType.Weierstrass)
            {
                BigInteger slope = p1.X == p2.X && p1.Y == p2.Y ? Mod((3 * p1.X * p1.X + a) * MulInverse(2 * p1.Y)) : Mod(Mod(p2.Y - p1.Y) * MulInverse(p2.X - p1.X));
                x3 = Mod((slope * slope) - p1.X - p2.X);
                y3 = Mod(-((slope * x3) + p1.Y - (slope * p1.X)));
            }
            else if (type == CurveType.Montgomery)
            {
                if ((p1.X == p2.X && p1.Y == p2.Y))
                {
                    BigInteger q = 3 * p1.X;
                    BigInteger w = q * p1.X;

                    BigInteger e = 2 * a;
                    BigInteger r = e * p1.X;

                    BigInteger t = 2 * b;
                    BigInteger y = t * p1.Y;

                    BigInteger u = MulInverse(y);

                    BigInteger o = w + e + 1;
                    BigInteger p = o * u;
                }
                BigInteger co = p1.X == p2.X && p1.Y == p2.Y ? Mod((3 * p1.X * p1.X + 2 * a * p1.X + 1) * MulInverse(2 * b * p1.Y)) : Mod(Mod(p2.Y - p1.Y) * MulInverse(p2.X - p1.X)); // Compute a commonly used coefficient
                x3 = Mod(b * co * co - a - p1.X - p2.X);
                y3 = Mod(((2 * p1.X + p2.X + a) * co) - (b * co * co * co) - p1.Y);
            }

            return new Point(x3, y3);
        }

        public Point Multiply(Point p, BigInteger scalar)
        {
            if (scalar <= 0) throw new Exception("Cannot multiply by a scalar which is <= 0");
            if (p == Point.POINT_AT_INFINITY) return Point.POINT_AT_INFINITY;

            Point p1 = new Point(p.X, p.Y);
            long high_bit = scalar.HighestBit() - 1;

            // Double-and-add method
            while (high_bit >= 0)
            {
                p1 = Add(p1, p1); // Double
                if ((scalar.BitAt(high_bit)))
                    p1 = Add(p1, p); // Add
                --high_bit;
            }

            return p1;
        }

        protected BigInteger MulInverse(BigInteger eq) => MulInverse(eq, modulo);
        public static BigInteger MulInverse(BigInteger eq, BigInteger modulo)
        {
            eq = Mod(eq, modulo);
            Stack<BigInteger> collect = new Stack<BigInteger>();
            BigInteger v = modulo; // Copy modulo
            BigInteger m;
            while ((m = v % eq) != 0)
            {
                collect.Push(-(v/eq));
                v = eq;
                eq = m;
            }
            if (collect.Count == 0) return 1;
            v = 1;
            m = collect.Pop();
            while (collect.Count > 0)
            {
                eq = m;
                m = v + (m * collect.Pop());
                v = eq;
            }
            return Mod(m, modulo);
        }

        public Point Inverse(Point p) => Inverse(p, modulo);
        protected static Point Inverse(Point p, BigInteger modulo) => new Point(p.X, Mod(-p.Y, modulo));

        public bool IsOnCurve(Point p)
        {
            try { CheckOnCurve(p); }
            catch { return false; }
            return true;
        }
        protected void CheckOnCurve(Point p)
        {
            if (
                p != Point.POINT_AT_INFINITY &&                                                                           // The point at infinity is asserted to be on the curve
                (type == CurveType.Weierstrass && Mod(p.Y * p.Y) != Mod((p.X * p.X * p.X) + (p.X * a) + b)) ||          // Weierstrass formula
                (type == CurveType.Montgomery && Mod(b * p.Y * p.Y) != Mod((p.X * p.X * p.X) + (p.X * p.X * a) + p.X))  // Montgomery formula
                ) throw new Exception("Point is not on curve");
        }

        protected BigInteger Mod(BigInteger b) => Mod(b, modulo);

        private static BigInteger Mod(BigInteger x, BigInteger m)
        {
            BigInteger r; ;
            if (x.Abs() >= m) r = x % m;
            else r = x;
            return r < 0 ? r + m : r;
        }

        // Efficient modular square root function
        public static BigInteger ShanksTonelli(BigInteger a, BigInteger prime)
        {
            if (prime < 3 || ModPow(a, (prime - 1) / 2, prime) != 1) return 0;
            Random rand = new Random();
            int e = 0;
            while ((prime & 1) != 1)
            {
                prime >>= 1;
                e += 1;
            }
            BigInteger s = prime / BigInteger.Pow(2, e);
            return 0;
        }

        protected static BigInteger ModPow(BigInteger x, BigInteger power, BigInteger prime)
        {
            BigInteger result = 1;
            bool setBit = false;
            while (power > 0)
            {
                x %= prime;
                setBit = (power & 1) == 1;
                power >>= 1;
                if (setBit) result *= x;
                x *= x;
            }

            return result;
        }
    }

}
