using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms
{
    public class Region
    {
        protected readonly List<Rectangle> region = new List<Rectangle>();

        public int Area
        {
            get
            {
                int total = 0;
                foreach (var rect in region)
                    total += (rect.Left - rect.Right) * (rect.Top - rect.Bottom);
                return total;
            }
        }
        public Rectangle[] SubRegions => region.ToArray();

        public Region(params Rectangle[] rectangles)
        {
            if (rectangles.Length > 0) region.Add(rectangles[0]);
            for (int i = 1; i < rectangles.Length; ++i) Add(rectangles[i]);
        }

        public Region(List<Rectangle> region) => this.region.AddRange(region);
        public Region(Region r) => this.region.AddRange(r.region);

        public Region Add(Rectangle rect)
        {
            Region r = new Region(region);
            r.IAdd(rect);
            return r;
        }

        protected void IAdd(Rectangle rect)
        {
            List<Rectangle> recompute = new List<Rectangle>();
            foreach (var rectangle in region) recompute.AddRange(rectangle.Subtract(rect));
            recompute.Add(rect);
            region.Clear();
            region.AddRange(recompute);
        }

        public Region Add(Region region)
        {
            Region r = new Region(this);
            foreach (var rectangle in region.region) r.IAdd(rectangle);
            return r;
        }

        public Region Subtract(Rectangle rect)
        {
            Region r = new Region(region);
            r.ISubtract(rect);
            return r;
        }

        protected void ISubtract(Rectangle rect)
        {
            List<Rectangle> recompute = new List<Rectangle>();
            foreach (var rectangle in region) recompute.AddRange(rectangle.Subtract(rect));
            region.Clear();
            region.AddRange(recompute);
        }

        public Region Subtract(Region region)
        {
            Region r = new Region(this);
            foreach (var rectangle in region.region) r.ISubtract(rectangle);
            return r;
        }

        public void Offset(Tuple<int, int> xy) => Offset(xy.Item1, xy.Item2);
        public void Offset(int x, int y)
        {
            foreach (var rect in region) rect.Offset(x, y);
        }
    }
}
