using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms
{
    public class Rectangle
    {
        public int Top { get; internal set; }
        public int Bottom { get; internal set; }
        public int Left { get; internal set; }
        public int Right { get; internal set; }
        public Rectangle(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public bool Intersects(Rectangle rect) => ((Left < rect.Right && Right >= rect.Left) || (Left <= rect.Right && Right > rect.Left)) && ((Top < rect.Bottom && Bottom >= rect.Top) || (Top <= rect.Bottom && Bottom > rect.Top));
        public bool Occludes(Rectangle rect) => Top >= rect.Top && Right >= rect.Right && Left >= rect.Left && Bottom >= rect.Bottom;
        public Rectangle GetIntersecting(Rectangle rect)
            => Intersects(rect) ?
            new Rectangle(
                Math.Max(Left, rect.Left),
                Math.Max(rect.Top, Top),
                Math.Min(rect.Right, Right),
                Math.Min(Bottom, rect.Bottom)
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
                components[rectangles++] = new Rectangle(intersect.Right, Math.Max(intersect.Top, Top), Right, Math.Min(intersect.Bottom, Bottom));
            if (intersect.Top > Top)
                components[rectangles++] = new Rectangle(Left, Top, Right, intersect.Top);
            if (intersect.Bottom < Bottom)
                components[rectangles] = new Rectangle(Left, intersect.Bottom, Right, Bottom);

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
