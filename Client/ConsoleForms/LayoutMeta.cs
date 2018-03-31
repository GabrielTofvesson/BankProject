using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms.Graphics
{
    // Computes a Left and Top value for some specified window parameters
    public delegate Tuple<int, int> PositionManager(int screenWidth, int screenHeight);
    public sealed class LayoutMeta
    {
        private readonly PositionManager manager;
        public LayoutMeta(PositionManager manager)
        {
            this.manager = manager;
        }

        public Tuple<int, int> ComputeLayoutParams(int width, int height) => manager(width, height);

        public static LayoutMeta Centering(View view) => new LayoutMeta(
            (w, h) =>
            new Tuple<int, int>(
                SpaceMaths.CenterPad(Console.WindowWidth, view.ContentWidth).Item1,
                SpaceMaths.CenterPad(Console.WindowHeight, view.ContentHeight + 1).Item1
                )
            );
    }
}
