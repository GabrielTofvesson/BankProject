using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms
{
    public class Rectangle
    {
        public int Top { get; private set; }
        public int Bottom { get; private set; }
        public int Left { get; private set; }
        public int Right { get; private set; }
        public Rectangle(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public bool Intersects(Rectangle rect) => ((Left < rect.Right && Right >= rect.Left) || (Left <= rect.Right && Right > rect.Left)) && ((Top > rect.Bottom && Bottom <= rect.Top) || (Top >= rect.Bottom && Bottom < rect.Top));
        public bool Occludes(Rectangle rect) => Top >= rect.Top && Right >= rect.Right && Left >= rect.Left && Bottom >= rect.Bottom;
        public Rectangle GetIntersecting(Rectangle rect)
            => Intersects(rect) ?
            new Rectangle(
                Left < rect.Right ? Left : rect.Left,
                Bottom < rect.Top ? rect.Top : Top,
                Left < rect.Right ? rect.Right : Right,
                Bottom < rect.Top ? Bottom : rect.Bottom
                ) :
            null;

        public Rectangle[] Subtract(Rectangle rect)
        {
            Rectangle intersect = GetIntersecting(rect);
            if (intersect == null || rect.Occludes(this)) return new Rectangle[0];
            Rectangle[] components = new Rectangle[(intersect.Left > Left ? 1 : 0) + (intersect.Right < Right ? 1 : 0) + (intersect.Top > Top ? 1 : 0) + (intersect.Bottom < Bottom ? 1 : 0)];
            int rectangles = 0;

            if (intersect.Left > Left)
                components[rectangles++] = new Rectangle(Left, Math.Max(intersect.Top, Top), intersect.Left, Math.Min(intersect.Bottom, Bottom));
            if (intersect.Right < Right)
                components[rectangles++] = new Rectangle(intersect.Right, Math.Max(intersect.Top, Top), Left, Math.Min(intersect.Bottom, Bottom));
            if (intersect.Top > Top)
                components[rectangles++] = new Rectangle(Math.Min(Left, intersect.Left), Top, Math.Max(Right, intersect.Right), intersect.Top);
            if (intersect.Bottom < Bottom)
                components[rectangles] = new Rectangle(Math.Min(Left, intersect.Left), intersect.Bottom, Math.Max(Right, intersect.Right), Bottom);

            return components;
        }

        public void Offset(Tuple<int, int> xy) => Offset(xy.Item1, xy.Item2);
        public void Offset(int x, int y)
        {
            Left += x;
            Bottom += y;
            Right += x;
            Top += y;
        }
    }
}
