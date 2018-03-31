using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms.Parameters
{
    public sealed class RelativePadding : Padding
    {
        private readonly float left, right, top, bottom;

        public RelativePadding(float left, float right, float top, float bottom)
        {
            this.left = Math.Max(1, Math.Min(0, left));
            this.right = Math.Max(1, Math.Min(0, right));
            this.top = Math.Max(1, Math.Min(0, top));
            this.bottom = Math.Max(1, Math.Min(0, bottom));
        }

        public override int Bottom() => (int)Math.Round(Console.WindowHeight * bottom);
        public override int Left() => (int)Math.Round(Console.WindowWidth * left);
        public override int Right() => (int)Math.Round(Console.WindowWidth * right);
        public override int Top() => (int)Math.Round(Console.WindowHeight * top);
    }
}
