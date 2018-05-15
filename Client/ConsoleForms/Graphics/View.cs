using Client.ConsoleForms;
using Client.ConsoleForms.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;

namespace Client.ConsoleForms.Graphics
{
    public abstract class View
    {
        protected delegate void EventAction();
        public delegate void ViewEvent(View v);

        protected static readonly Padding DEFAULT_PADDING = new AbsolutePadding(0, 0, 0, 0);

        protected internal readonly Padding padding;
        protected readonly Gravity gravity;
        protected readonly bool vCenter, hCenter;
        protected readonly string back_data;

        public char Border { get; set; }
        public bool DrawBorder { get; set; }
        public ConsoleColor BorderColor { get; set; }
        public ConsoleColor BackgroundColor { get; set; }
        public ConsoleColor TextColor { get; set; }
        public int ContentWidth { get; protected set; }
        public int ContentHeight { get; protected set; }
        public abstract Region Occlusion { get; }
        public bool Dirty { get; set; }
        public LangManager I18n { get; private set; }
        public ViewEvent OnBackEvent { get; set; }
        public ViewEvent OnClose { get; set; }

        public View(ViewData parameters, LangManager lang)
        {
            padding = new AbsolutePadding(parameters.AttribueAsInt("padding_left"), parameters.AttribueAsInt("padding_right"), parameters.AttribueAsInt("padding_top"), parameters.AttribueAsInt("padding_bottom"));
            gravity = (Gravity)parameters.AttribueAsInt("gravity");
            BorderColor = (ConsoleColor)parameters.AttribueAsInt("border", (int)ConsoleColor.Blue);
            BackgroundColor = (ConsoleColor)parameters.AttribueAsInt("color_background", (int)ConsoleColor.White);
            TextColor = (ConsoleColor)parameters.AttribueAsInt("color_text", (int)ConsoleColor.Black);
            Border = ' ';
            DrawBorder = true;// parameters.attributes.ContainsKey("border");
            I18n = lang;

            back_data = parameters.GetAttribute("back");

            // Do check to ensure that gravity flags are valid
            Enums.LayoutCheck(ref gravity);
            vCenter = !Enums.HasFlag(gravity, Gravity.LEFT) && !Enums.HasFlag(gravity, Gravity.RIGHT);
            hCenter = !Enums.HasFlag(gravity, Gravity.TOP) && !Enums.HasFlag(gravity, Gravity.BOTTOM);
        }

        public void ResetRenderColors() => SetRenderColors(BackgroundColor, TextColor);
        public void SetRenderColors(ConsoleColor bg, ConsoleColor fg)
        {
            Console.BackgroundColor = bg;
            Console.ForegroundColor = fg;
        }

        public void Draw(Tuple<int, int> t) => Draw(t.Item1, t.Item2);
        public void Draw(int left, int top) => Draw(left, ref top);
        public void Draw(int left, ref int top)
        {
            Dirty = false;
            if (DrawBorder)
                _DrawBorder(left, top);
            DrawPadding(ref left, ref top);
            _Draw(left, ref top);
        }
        public virtual void _DrawBorder(int left, int top)
        {
            Console.BackgroundColor = BorderColor;
            Console.SetCursorPosition(left - 1 - padding.Left(), top - 1 - padding.Top());
            Console.Write(Filler(Border, ContentWidth + padding.Left() + padding.Right() + 4));
            for (int i = 0; i < ContentHeight + padding.Top() + padding.Bottom(); ++i)
            {
                Console.SetCursorPosition(left - padding.Left() - 1, top - padding.Top() + i);
                Console.Write(Filler(Border, 2));
                Console.SetCursorPosition(left + ContentWidth + padding.Left() + padding.Right() - 1, top - padding.Top() + i);
                Console.Write(Filler(Border, 2));
            }
            Console.SetCursorPosition(left - padding.Left() - 1, top + ContentHeight + padding.Bottom());
            Console.Write(Filler(Border, ContentWidth + padding.Left() + padding.Right() + 4));
            Console.BackgroundColor = ConsoleColor.Black;
        }
        public virtual void DrawPadding(ref int left, ref int top)
        {
            Console.BackgroundColor = BackgroundColor;
            // Top padding
            for(int i = 0; i<padding.Top(); ++i)
            {
                Console.SetCursorPosition(left - padding.Left() + 1, top + i - padding.Top());
                Console.Write(Filler(' ', padding.Left() + ContentWidth + padding.Right()));
            }

            // Left-right padding
            for(int i = 0; i<ContentHeight; ++i)
            {
                Console.SetCursorPosition(left - padding.Left() + 1, top + i);
                Console.Write(Filler(' ', padding.Left()));
                Console.SetCursorPosition(left + ContentWidth + padding.Left() - 1, top + i);
                Console.Write(Filler(' ', padding.Right()));
            }

            // Bottom padding
            for(int i = 0; i<padding.Bottom(); ++i)
            {
                Console.SetCursorPosition(left - 1, top + ContentHeight + i);
                Console.Write(Filler(' ', padding.Left() + ContentWidth + padding.Right()));
            }

            left += padding.Left() / 2; // Increment left offset
        }
        protected abstract void _Draw(int left, ref int top);
        public virtual bool HandleKeyEvent(ConsoleController.KeyEvent info, bool inFocus, bool triggered)
        {
            if ((back_data.Length != 0 || OnBackEvent!=null) && (triggered || (info.ValidEvent && inFocus)) && info.Event.Key == ConsoleKey.Escape)
            {
                info.ValidEvent = false;
                if(back_data.Length!=0) ParseAction(back_data, true)();
                OnBackEvent?.Invoke(this);
            }
            return false;
        }
        public virtual void TriggerKeyEvent(ConsoleController.KeyEvent info) => HandleKeyEvent(info, true, true);
        protected void DrawTopPadding(int left, ref int top) => DrawPadding(left, ref top, padding.Top());
        protected void DrawBottomPadding(int left, ref int top) => DrawPadding(left, ref top, padding.Bottom());
        private void DrawPadding(int left, ref int top, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                Console.SetCursorPosition(left, top++);
                Console.Write(Filler(' ', ContentWidth));
            }
        }
        protected EventAction ParseAction(ViewData data)
        {
            bool.TryParse(data.GetAttribute("close"), out bool close);
            return ParseAction(data.GetAttribute("event"), close);
        }
        protected EventAction ParseAction(string action, bool close)
        {
            string[] components;
            if (action == null || !action.Contains(':') || (components = action.Split(':')).Length != 2) return () => { };
            var views = ConsoleController.LoadResourceViews(components[0], I18n);
            var view = views.GetNamed(components[1]);
            return () =>
            {
                if (close) ConsoleController.singleton.CloseView(this);
                ConsoleController.singleton.AddView(view);
            };
        }

        protected internal static string Filler(char c, int count)
        {
            if (count == 0) return "";
            StringBuilder builder = new StringBuilder(count);
            for (int i = 0; i < count; ++i) builder.Append(c);
            return builder.ToString();
        }
    }
}
