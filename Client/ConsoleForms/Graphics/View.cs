﻿using Client.ConsoleForms;
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

        protected static readonly Padding DEFAULT_PADDING = new AbsolutePadding(0, 0, 0, 0);

        protected readonly Padding padding;
        protected readonly Gravity gravity;
        protected readonly bool vCenter, hCenter;
        protected readonly string back_data;

        public char Border { get; set; }
        public bool DrawBorder { get; set; }
        public ConsoleColor BorderColor { get; set; }
        public int ContentWidth { get; protected set; }
        public int ContentHeight { get; protected set; }
        public abstract Region Occlusion { get; }
        public bool Dirty { get; set; }

        public View(ViewData parameters)
        {
            this.padding = new AbsolutePadding(parameters.AttribueAsInt("padding_left"), parameters.AttribueAsInt("padding_right"), parameters.AttribueAsInt("padding_top"), parameters.AttribueAsInt("padding_bottom"));
            this.gravity = (Gravity)parameters.AttribueAsInt("gravity");
            this.BorderColor = (ConsoleColor)parameters.AttribueAsInt("border", (int)ConsoleColor.Blue);
            this.Border = ' ';
            DrawBorder = parameters.attributes.ContainsKey("border");

            back_data = parameters.GetAttribute("back");

            // Do check to ensure that gravity flags are valid
            Enums.LayoutCheck(ref gravity);
            vCenter = !Enums.HasFlag(gravity, Gravity.LEFT) && !Enums.HasFlag(gravity, Gravity.RIGHT);
            hCenter = !Enums.HasFlag(gravity, Gravity.TOP) && !Enums.HasFlag(gravity, Gravity.BOTTOM);
        }

        public void Draw(Tuple<int, int> t) => Draw(t.Item1, t.Item2);
        public void Draw(int left, int top)
        {
            Dirty = false;
            if (DrawBorder) _DrawBorder(left, top);
            _Draw(left + 1, top);
        }
        public virtual void _DrawBorder(int left, int top)
        {
            Console.BackgroundColor = BorderColor;
            Console.SetCursorPosition(left, top - 1);
            Console.Write(Filler(Border, ContentWidth + 1));
            for (int i = -1; i < ContentHeight; ++i)
            {
                Console.SetCursorPosition(left, top + i);
                Console.Write(Filler(Border, 2));
                Console.SetCursorPosition(left + ContentWidth, top + i);
                Console.Write(Filler(Border, 2));
            }
            Console.SetCursorPosition(left, top + ContentHeight);
            Console.Write(Filler(Border, ContentWidth + 2));
            Console.BackgroundColor = ConsoleColor.Black;
        }
        protected abstract void _Draw(int left, int top);
        public virtual bool HandleKeyEvent(ConsoleController.KeyEvent info, bool inFocus)
        {
            if (back_data.Length != 0 && info.ValidEvent && inFocus && info.Event.Key == ConsoleKey.Escape)
            {
                info.ValidEvent = false;
                ParseAction(back_data, true)();
            }
            return false;
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
            var views = ConsoleController.LoadResourceViews(components[0]);
            var view = views.GetNamed(components[1]);
            return () =>
            {
                if (close) ConsoleController.singleton.CloseView(this);
                ConsoleController.singleton.AddView(view);
            };
        }

        protected static string Filler(char c, int count)
        {
            if (count == 0) return "";
            StringBuilder builder = new StringBuilder(count);
            for (int i = 0; i < count; ++i) builder.Append(c);
            return builder.ToString();
        }
    }
}