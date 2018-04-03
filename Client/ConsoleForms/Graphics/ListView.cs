using Client.ConsoleForms.Parameters;
using System;
using System.Collections.Generic;

namespace Client.ConsoleForms.Graphics
{
    public class ListView : View
    {
        protected readonly List<Tuple<string, View>> innerViews = new List<Tuple<string, View>>();

        public int SelectedView { get; set; }
        public int ViewCount { get => innerViews.Count; }
        public ConsoleColor SelectBackground { get; set; }
        public ConsoleColor SelectText { get; set; }

        public override Region Occlusion => new Region(new Rectangle(0, 0, ContentWidth, ContentHeight));

        public ListView(ViewData parameters) : base(parameters)
        {
            SelectBackground = (ConsoleColor)parameters.AttribueAsInt("background_select_color", (int)ConsoleColor.Gray);
            SelectText = (ConsoleColor)parameters.AttribueAsInt("text_select_color", (int)ConsoleColor.Gray);


            int maxWidth = parameters.AttribueAsInt("width", -1);
            bool limited = maxWidth != -1;

            foreach (var view in parameters.nestedData.FirstOrNull(n => n.Name.Equals("Views"))?.nestedData ?? new List<ViewData>())
            {
                // Limit content width
                if (limited && view.AttribueAsInt("width") > maxWidth) view.attributes["width"] = maxWidth.ToString();

                Tuple<string, View> v = ConsoleController.LoadView(parameters.attributes["xmlns"], view); // Load the view in with standard namespace
                innerViews.Add(v);

                if (!limited) maxWidth = Math.Max(v.Item2.ContentWidth, maxWidth);

                ContentHeight += v.Item2.ContentHeight + 1;
            }
            ++ContentHeight;

            SelectedView = 0;

            ContentWidth = maxWidth;
        }

        public View GetView(string name) => innerViews.FirstOrNull(v => v.Item1.Equals(name))?.Item2;

        protected override void _Draw(int left, ref int top)
        {
            foreach(var view in innerViews)
            {
                DrawBlankLine(left, ref top);
                ConsoleColor
                    bgHold = view.Item2.BackgroundColor,
                    fgHold = view.Item2.TextColor;

                if(view == innerViews[SelectedView])
                {
                    view.Item2.BackgroundColor = SelectBackground;
                    view.Item2.TextColor = SelectText;
                }

                DrawView(left, ref top, view.Item2);

                if (view == innerViews[SelectedView])
                {
                    view.Item2.BackgroundColor = bgHold;
                    view.Item2.TextColor = fgHold;
                }
            }
            DrawBlankLine(left, ref top);
        }

        protected virtual void DrawView(int left, ref int top, View v) => v.Draw(left, ref top);

        protected virtual void DrawBlankLine(int left, ref int top)
        {
            ResetRenderColors();
            Console.SetCursorPosition(left, top++);
            Console.Write(Filler(' ', ContentWidth));
        }

        public override bool HandleKeyEvent(ConsoleController.KeyEvent info, bool inFocus)
        {
            if (!inFocus) return false;
            if (innerViews[SelectedView].Item2.HandleKeyEvent(info, inFocus)) return true;
            else if (!info.ValidEvent) return false;

            // Handle navigation
            switch (info.Event.Key)
            {
                case ConsoleKey.UpArrow:
                    if (SelectedView > 0)
                    {
                        info.ValidEvent = false;
                        --SelectedView;
                        return true;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if(SelectedView < innerViews.Count - 1)
                    {
                        info.ValidEvent = false;
                        ++SelectedView;
                        return true;
                    }
                    break;
            }

            return base.HandleKeyEvent(info, inFocus);
        }
    }
}
