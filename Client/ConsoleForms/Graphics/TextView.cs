using Client.ConsoleForms.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;

namespace Client.ConsoleForms.Graphics
{
    public class TextView : View
    {
        protected readonly string[] text;
        protected string[] text_render;
        protected int maxWidth, maxHeight;

        public int MaxWidth
        {
            get => maxWidth;

            set
            {
                maxWidth = value;
                text_render = ComputeTextDimensions(text);
                Dirty = true;
            }
        }
        public int MaxHeight
        {
            get => maxHeight;

            set
            {
                maxHeight = value;
                text_render = ComputeTextDimensions(text);
                Dirty = true;
            }
        }
        public override Region Occlusion => new Region(new Rectangle(0, -1, ContentWidth + 2, ContentHeight));

        //public char Border { get; set; }
        //public ConsoleColor BorderColor { get; set; }

        public TextView(ViewData parameters, LangManager lang) : base(parameters, lang)
        {
            //BorderColor = (ConsoleColor) parameters.AttribueAsInt("border", (int)ConsoleColor.Blue);

            Border = ' ';
            this.text = parameters.NestedText("Text").Split(' ');
            int widest = 0;
            foreach (var t in parameters.NestedText("Text").Split('\n'))
                if (t.Length > widest)
                    widest = t.Length;
            this.maxWidth = parameters.AttribueAsInt("width") < 1 ? widest : parameters.AttribueAsInt("width");
            this.maxHeight = parameters.AttribueAsInt("height", -1);

            // Compute the layout of the text to be rendered
            text_render = ComputeTextDimensions(this.text);
            int actualWidth = 0;
            foreach (var t in text_render) if (actualWidth < t.Length) actualWidth = t.Length;
            ContentWidth = maxWidth + padding.Left() + padding.Right();
            ContentHeight = text_render.Length + padding.Top() + padding.Bottom();
        }

        protected virtual string[] ComputeTextDimensions(string[] text)
        {
            if (maxHeight == 0)
                return new string[0];

            BoundedList<string> generate = new BoundedList<string>(maxHeight);

            for (int i = 0; i < text.Length; ++i)
            {
                if (generate.Count == 0)
                {
                    string[] split = Subsplit(text[i], maxWidth);
                    for (int j = 0; j < split.Length; ++j)
                        if (!generate.Add(split[j]))
                            goto Generated;
                }
                else
                {
                    if (WillSubSplit(text[i], maxWidth))
                    {
                        int startAdd = 0;
                        string[] split;
                        if (generate[generate.Count - 1].Length != maxWidth)
                        {
                            startAdd = 1;
                            split = Subsplit(generate[generate.Count - 1] + " " + text[i], maxWidth);
                            generate[generate.Count - 1] = split[0];
                        }
                        else split = Subsplit(text[i], maxWidth);
                        for (int j = startAdd; j < split.Length; ++j)
                            if (!generate.Add(split[j]))
                                goto Generated;
                    }
                    else
                    {
                        if (generate[generate.Count - 1].Length + text[i].Length < maxWidth)
                            generate[generate.Count - 1] += " " + text[i];
                        else if (!generate.Add(text[i]))
                            break;
                    }
                }
            }

            Generated:
            return generate.ToArray();
        }

        private static string[] Subsplit(string s, int max)
        {
            int nlCount = 0;
            for (int i = 0; i < s.Length; ++i) if (s[i] == '\n') ++nlCount;

            string[] result = new string[((s.Length - nlCount) / max) + nlCount + ((s.Length - nlCount) % max != 0 ? 1 : 0)];

            int read = 0;
            for (int i = 0; i < result.Length; ++i)
            {
                StringBuilder subCollect = new StringBuilder();
                int idx = read;
                int valid = 0;
                while (idx < s.Length && valid < max)
                {
                    char c = s[idx];
                    subCollect.Append(c);
                    ++idx;
                    if (c != '\n') ++valid;
                }
                string sub = subCollect.ToString();
                if (sub.Contains('\n'))
                {
                    while (sub.Contains('\n'))
                    {
                        result[i++] = sub.Substring(0, sub.IndexOf('\n'));
                        sub = sub.Substring(sub.IndexOf('\n') + 1);
                    }
                    if (i < result.Length) result[i] = sub;
                }
                else result[i] = s.Substring(read, Math.Min(s.Length - read, read + max));
                read += valid;
            }
            return result;
        }

        private static bool WillSubSplit(string s, int max) => ((s.Length / max) + (s.Length % max != 0 ? 1 : 0)) > 1 || s.Contains('\n');

        protected override void _Draw(int left, ref int top)
        {
            DrawEmptyPadding(left, ref top, padding.Top());
            DrawContent(left, ref top);
            DrawEmptyPadding(left, ref top, padding.Bottom());
        }

        protected void DrawContent(int left, ref int top)
        {
            int pl = padding.Left(), pr = padding.Right();
            Console.BackgroundColor = BackgroundColor;
            Console.ForegroundColor = TextColor;
            for (int i = 0; i < text_render.Length; ++i)
            {
                Console.SetCursorPosition(left, top++);
                Console.Write(Filler(' ', pl) + text_render[i] + Filler(' ', MaxWidth - text_render[i].Length) + Filler(' ', pr));
            }
        }

        protected void DrawEmptyPadding(int left, ref int top, int padHeight)
        {
            int pl = padding.Left(), pr = padding.Right();
            for (int i = padHeight; i > 0; --i)
            {
                Console.SetCursorPosition(left, top++);
                Console.BackgroundColor = BackgroundColor;
                Console.Write(Filler(' ', maxWidth + pl + pr));
            }
        }

        public override bool HandleKeyEvent(ConsoleController.KeyEvent info, bool inFocus) => base.HandleKeyEvent(info, inFocus);
    }
}
