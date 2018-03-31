using Client.ConsoleForms.Parameters;
using System;
using System.Diagnostics;

namespace Client.ConsoleForms
{
    // Enum helper class
    public static class Enums
    {
        public static void LayoutCheck(ref Gravity g)
        {
            if (!IsValidFlag(g))
            {
#if STRICT_LAYOUT
                throw new LayoutParameterException();
#else
                Debug.WriteLine($"Invalid layout parameters {{{g}}}:\n{Environment.StackTrace}\n");
                g = 0;
#endif
            }
        }
        public static bool HasFlag(Gravity value, Gravity flag) => (value & flag) == flag;
        public static bool IsValidFlag(Gravity g) =>
            !(
            (HasFlag(g, Gravity.LEFT) && HasFlag(g, Gravity.RIGHT)) ||   // Gravity cannot be both LEFT and RIGHT
            (HasFlag(g, Gravity.TOP) && HasFlag(g, Gravity.BOTTOM))      // Gravity cannot be both TOP and BOTTOM
            );
    }

    // Miscellaneous extensions methods
    public static class Extensions
    {
        public static int CollectiveLength(this ViewData[] data)
        {
            int len = 0;
            foreach (var val in data)
                len += val?.InnerText.Length ?? 0;
            return len;
        }
    }


    // Miscellaneous graphics helpers
    public static class SpaceMaths
    {
        public static Tuple<int, int> CenterPad(int maxLength, int contentLength)
        {
            int pad = maxLength - contentLength;
            return new Tuple<int, int>(pad / 2, pad - (pad / 2));
        }
    }
}
