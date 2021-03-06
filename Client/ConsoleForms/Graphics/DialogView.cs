﻿using Client.ConsoleForms.Parameters;
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
        protected readonly int optionsWidth;
        protected int select;
        protected SelectListener listener;

        public int Select
        {
            get => select;
            set => select = value < 0 ? 0 : value >= options.Length ? options.Length - 1 : value;
        }

        public override string Text
        {
            get => base.Text;
            set 
            {
                base.Text = value;
                // Since setting the text triggers a rendering recomputation for TextView, we have to recompute rendering for options too
                if (optionsWidth > ContentWidth) ContentWidth = optionsWidth;
                ContentHeight += 2;
            }
        }
        /*
        public override Region Occlusion => new Region(
            new Rectangle(
                -padding.Left() - (DrawBorder ? 2 : 0),                 // Left bound
                -padding.Top() - (DrawBorder ? 1 : 0),                  // Top bound
                ContentWidth + padding.Right() + (DrawBorder ? 2 : 0),  // Right bound
                ContentHeight + padding.Bottom() + (DrawBorder ? 1 : 0) // Bottom bound
                )
            );
            */
        public ConsoleColor SelectColor { get; set; }
        public ConsoleColor NotSelectColor { get; set; }
        public string[] Options { get => options.Transform(d => d.InnerText); }

        private static int ComputeLength(Tuple<string, string>[] opts) => opts.CollectiveLength(true) + opts.Length - 1;

        public DialogView(ViewData parameters, LangManager lang) :
            base(parameters/*.SetAttribute("width",
                Math.Max(
                    parameters.AttribueAsInt("width") < 1 ? parameters.NestedText("Text").Length : parameters.AttribueAsInt("width"),
                    ComputeLength(parameters.Get("Options")?.CollectSub("Option") ?? new Tuple<string, string>[0])
                ))*/, lang)
        {
            ViewData optionsData = parameters.Get("Options");
            this.options = optionsData.nestedData.Filter(p => p.Name.Equals("Option")).ToArray();
            this.select = parameters.AttribueAsInt("select");
            //ContentHeight += 2;
            select = select < 0 ? 0 : select >= options.Length ? 0 : select;
            SelectColor = (ConsoleColor)parameters.AttribueAsInt("select_color", (int)ConsoleColor.Gray);
            NotSelectColor = (ConsoleColor)parameters.AttribueAsInt("unselect_color", (int)ConsoleColor.White);
            optionsWidth = ComputeLength(parameters.Get("Options")?.CollectSub("Option") ?? new Tuple<string, string>[0]);
            if (optionsWidth > ContentWidth) ContentWidth = optionsWidth;
        }

        protected override void _Draw(int left, ref int top)
        {
            base.DrawContent(left, ref top);
            DrawEmptyPadding(left, ref top, 1);
            DrawOptions(left, ref top);
        }

        protected virtual void DrawOptions(int left, ref int top)
        {
            Console.SetCursorPosition(left, top++);

            int pad = ContentWidth - options.CollectiveLength() - options.Length;
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

        public override bool HandleKeyEvent(ConsoleController.KeyEvent evt, bool inFocus, bool triggered)
        {
            bool changed = base.HandleKeyEvent(evt, inFocus, triggered);
            ConsoleKeyInfo info = evt.Event;
            if (!triggered && (!evt.ValidEvent || !inFocus)) return changed;
            evt.ValidEvent = false; // Invalidate event
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
