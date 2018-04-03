using Client.ConsoleForms.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;

namespace Client.ConsoleForms.Graphics
{
    public class DialogView : TextView
    {
        public delegate void SelectListener(DialogView view, int selectionIndex, string selection);

        protected readonly ViewData[] options;
        protected int select;
        protected SelectListener listener;

        public int Select
        {
            get => select;
            set => select = value < 0 ? 0 : value >= options.Length ? options.Length - 1 : value;
        }
        public override Region Occlusion => new Region(new Rectangle(0, -1, ContentWidth + 2, ContentHeight + 2));

        public ConsoleColor SelectColor { get; set; }
        public ConsoleColor NotSelectColor { get; set; }
        public string[] Options { get => options.Transform(d => d.InnerText); }

        private static int ComputeLength(Tuple<string, string>[] opts) => opts.CollectiveLength(true) + opts.Length - 1;

        public DialogView(ViewData parameters) :
            base(parameters.SetAttribute("width",
                Math.Max(
                    parameters.AttribueAsInt("width") < 1 ? parameters.NestedText("Text").Length : parameters.AttribueAsInt("width"),
                    ComputeLength(parameters.Get("Options").CollectSub("Option"))
                )))
        {
            ViewData optionsData = parameters.Get("Options");
            this.options = optionsData.nestedData.Filter(p => p.Name.Equals("Option")).ToArray();
            this.select = parameters.AttribueAsInt("select");
            ContentHeight += 2;
            select = select < 0 ? 0 : select >= options.Length ? 0 : select;
            SelectColor = (ConsoleColor)parameters.AttribueAsInt("select_color", (int)ConsoleColor.Gray);
            NotSelectColor = (ConsoleColor)parameters.AttribueAsInt("unselect_color", (int)ConsoleColor.White);
        }

        protected override void _Draw(int left, ref int top)
        {
            DrawEmptyPadding(left, ref top, padding.Top());
            base.DrawContent(left, ref top);
            DrawEmptyPadding(left, ref top, 1);
            DrawOptions(left, ref top);
            DrawEmptyPadding(left, ref top, padding.Bottom());
        }

        protected virtual void DrawOptions(int left, ref int top)
        {
            int pl = padding.Left(), pr = padding.Right();
            Console.SetCursorPosition(left, top++);

            int pad = MaxWidth - options.CollectiveLength() - options.Length + pl + pr;
            int lpad = (int)(pad / 2f);
            Console.BackgroundColor = BackgroundColor;
            Console.Write(Filler(' ', lpad));
            for (int i = 0; i < options.Length; ++i)
            {
                Console.BackgroundColor = i == select ? SelectColor : NotSelectColor;
                Console.Write(options[i].InnerText);
                Console.BackgroundColor = BackgroundColor;
                Console.Write(' ');
            }
            Console.Write(Filler(' ', pad - lpad));
        }

        public override bool HandleKeyEvent(ConsoleController.KeyEvent evt, bool inFocus)
        {
            bool changed = base.HandleKeyEvent(evt, inFocus);
            ConsoleKeyInfo info = evt.Event;
            if (!evt.ValidEvent || !inFocus) return changed;
            switch (info.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (select > 0) --select;
                    break;
                case ConsoleKey.RightArrow:
                    if (select < options.Length - 1) ++select;
                    break;
                case ConsoleKey.Enter:
                    ParseAction(options[select])();
                    listener?.Invoke(this, select, options[select].InnerText);
                    return changed;
                default:
                    return changed;
            }
            return true;
        }

        public void RegisterSelectListener(SelectListener listener) => this.listener = listener;
    }
}
