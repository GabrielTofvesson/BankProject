using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms.Parameters
{
    public sealed class AbsolutePadding : Padding
    {
        private readonly int left, right, top, bottom;

        public AbsolutePadding(int left, int right, int top, int bottom)
        {
            this.left = Math.Max(0, left);
            this.right = Math.Max(0, right);
            this.top = Math.Max(0, top);
            this.bottom = Math.Max(0, bottom);
        }

        public override int Bottom() => bottom;
        public override int Left() => left;
        public override int Right() => right;
        public override int Top() => top;
    }
}
