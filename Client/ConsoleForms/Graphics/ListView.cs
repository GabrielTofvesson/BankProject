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
        private int maxWidth;
        private readonly bool limited;


        public override Region Occlusion => new Region(
            new Rectangle(
                -padding.Left() - (DrawBorder ? 2 : 0),                 // Left bound
                -padding.Top() - (DrawBorder ? 1 : 0),                  // Top bound
                ContentWidth + padding.Right() + (DrawBorder ? 2 : 0) + 1,  // Right bound
                ContentHeight + padding.Bottom() + (DrawBorder ? 1 : 0) // Bottom bound
                )
            );

        public ListView(ViewData parameters, LangManager lang) : base(parameters, lang)
        {
            SelectBackground = (ConsoleColor)parameters.AttribueAsInt("background_select_color", (int)ConsoleColor.Gray);
            SelectText = (ConsoleColor)parameters.AttribueAsInt("text_select_color", (int)ConsoleColor.Gray);


            maxWidth = parameters.AttribueAsInt("width", -1);
            limited = maxWidth != -1;

            foreach (var view in parameters.nestedData.FirstOrNull(n => n.Name.Equals("Views"))?.nestedData ?? new List<ViewData>())
            {
                // Limit content width
                if (limited && view.AttribueAsInt("width") > maxWidth) view.attributes["width"] = maxWidth.ToString();
                
                innerViews.Add(ConsoleController.LoadView(parameters.attributes["xmlns"], view, I18n)); // Load the view in with standard namespace
            }

            ComputeSize();

            SelectedView = 0;
        }


        // Optimized to add multiple view before recomputing size
        public void AddViews(params Tuple<string, View>[] data) => AddViews(0, data);
        public void AddViews(int insert, params Tuple<string, View>[] data)
        {
            int inIdx = insert;
            foreach (var datum in data)
            {
                datum.Item2.DrawBorder = false;
                _AddView(datum.Item2, datum.Item1, inIdx++);
            }
            ComputeSize();
        }
        // Add single view
        public void AddView(View v, string viewID, int insert = 0)
        {
            _AddView(v, viewID, insert);
            ComputeSize();
        }
        // Add view without recomputing layout size
        private void _AddView(View v, string viewID, int insert)
        {
            foreach (var data in innerViews)
                if (data.Item1 != null && data.Item1.Equals(viewID))
                    throw new SystemException("Cannot load view with same id"); // TODO: Replace with custom exception
            innerViews.Insert(Math.Min(insert, innerViews.Count), new Tuple<string, View>(viewID, v));
        }

        public bool RemoveView(string name)
        {
            for (int i = innerViews.Count - 1; i >= 0; --i)
                if (innerViews[i].Item1.Equals(name))
                {
                    innerViews.RemoveAt(i);
                    return true;
                }
            return false;
        }

        public bool RemoveView(View view)
        {
            for (int i = innerViews.Count - 1; i >= 0; --i)
                if (innerViews[i].Item2.Equals(view))
                {
                    innerViews.RemoveAt(i);
                    return true;
                }
            return false;
        }

        public void RemoveIf(Predicate<Tuple<string, View>> p)
        {
            for(int i = innerViews.Count - 1; i>=0; --i)
                if (p(innerViews[i]))
                    innerViews.RemoveAt(i);
        }

        protected void ComputeSize()
        {
            ContentHeight = 0;
            foreach(var v in innerViews)
            {
                v.Item2.DrawBorder = false;
                //innerViews.Add(v);

                if (!limited) maxWidth = Math.Max(v.Item2.ContentWidth, maxWidth);

                ContentHeight += v.Item2.ContentHeight + 1;
            }
            ++ContentHeight;

            ContentWidth = maxWidth;
        }

        public View GetView(string name) => innerViews.FirstOrNull(v => v.Item1.Equals(name))?.Item2;
        public T GetView<T>(string name) where T : View => (T)GetView(name);

        protected override void _Draw(int left, ref int top)
        {
            ++left;
            foreach(var view in innerViews)
            {
                DrawBlankLine(left - 1, ref top);
                ConsoleColor
                    bgHold = view.Item2.BackgroundColor,
                    fgHold = view.Item2.TextColor;

                if(view == innerViews[SelectedView])
                {
                    view.Item2.BackgroundColor = SelectBackground;
                    //view.Item2.TextColor = SelectText;
                }
                Region sub = new Region(new Rectangle(0, 0, ContentWidth, view.Item2.ContentHeight)).Subtract(view.Item2.Occlusion);

                sub.Offset(left - 1, top);

                ConsoleController.ClearRegion(sub, view.Item2.BackgroundColor);

                DrawView(left - 1, ref top, view.Item2);

                if (view == innerViews[SelectedView])
                {
                    view.Item2.BackgroundColor = bgHold;
                    view.Item2.TextColor = fgHold;
                }
            }
            DrawBlankLine(left - 1, ref top);
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
            if (!inFocus || !info.ValidEvent) return false;

            bool changed = base.HandleKeyEvent(info, inFocus) || innerViews[SelectedView].Item2.HandleKeyEvent(info, inFocus);
            info.ValidEvent = false;
            // Handle navigation
            switch (info.Event.Key)
            {
                case ConsoleKey.UpArrow:
                    if (SelectedView > 0)
                    {
                        --SelectedView;
                        return true;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if(SelectedView < innerViews.Count - 1)
                    {
                        ++SelectedView;
                        return true;
                    }
                    break;
            }

            return changed;
        }
    }
}
