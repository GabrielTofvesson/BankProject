using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms
{
    [Flags]
    public enum Gravity
    {
        LEFT = 1,
        RIGHT = 2,
        TOP = 4,
        BOTTOM = 8
    }
}
