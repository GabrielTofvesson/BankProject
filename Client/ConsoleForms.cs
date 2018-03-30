//#define STRICT_LAYOUT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Tofvesson.Crypto;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Reflection;
using Client.Properties;
using Tofvesson.Collections;
using System.Collections.ObjectModel;

namespace ConsoleForms
{
    [Flags]
    public enum Gravity
    {
        LEFT = 1,
        RIGHT = 2,
        TOP = 4,
        BOTTOM = 8
    }

    public abstract class Padding
    {
        public abstract int Left();
        public abstract int Right();
        public abstract int Top();
        public abstract int Bottom();
    }

    public sealed class RelativePadding : Padding
    {
        private readonly float left, right, top, bottom;

        public RelativePadding(float left, float right, float top, float bottom)
        {
            this.left = Math.Max(1, Math.Min(0, left));
            this.right = Math.Max(1, Math.Min(0, right));
            this.top = Math.Max(1, Math.Min(0, top));
            this.bottom = Math.Max(1, Math.Min(0, bottom));
        }

        public override int Bottom() => (int)Math.Round(Console.WindowHeight * bottom);
        public override int Left() => (int)Math.Round(Console.WindowWidth * left);
        public override int Right() => (int)Math.Round(Console.WindowWidth * right);
        public override int Top() => (int)Math.Round(Console.WindowHeight * top);
    }

    public sealed class AbsolutePadding : Padding
    {
        private readonly int left, right, top, bottom;

        public AbsolutePadding(int left, int right, int top, int bottom)
        {
            this.left = Math.Max(0, left);
            this.right = Math.Max(0, right);
            this.top = Math.Max(0, top);
            this.bottom = Math.Max(0, bottom);
        }

        public override int Bottom() => bottom;
        public override int Left() => left;
        public override int Right() => right;
        public override int Top() => top;
    }


    static class Enums
    {
        internal static void LayoutCheck(ref Gravity g)
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
        internal static bool HasFlag(Gravity value, Gravity flag) => (value & flag) == flag;
        internal static bool IsValidFlag(Gravity g) =>
            !(
            (HasFlag(g, Gravity.LEFT) && HasFlag(g, Gravity.RIGHT)) ||   // Gravity cannot be both LEFT and RIGHT
            (HasFlag(g, Gravity.TOP) && HasFlag(g, Gravity.BOTTOM))      // Gravity cannot be both TOP and BOTTOM
            );
    }

    // Handles graphics and rendering instrumentation
    public sealed class ConsoleController
    {
        public class KeyEvent
        {
            public bool ValidEvent { get; set; }
            public ConsoleKeyInfo Event { get; }

            internal KeyEvent(ConsoleKeyInfo info) { Event = info; ValidEvent = true; }
        }

        public static readonly ConsoleController singleton = new ConsoleController();

        private readonly List<Tuple<View, LayoutMeta>> renderQueue = new List<Tuple<View, LayoutMeta>>();
        private int width = Console.WindowWidth, height = Console.WindowHeight;
        private CancellationPipe cancel;
        private Task resizeListener;

        public bool Dirty
        {
            get
            {
                Region occlusion = new Region();
                for (int i = renderQueue.Count - 1; i >= 0; --i)
                {
                    Tuple<int, int> lParams = renderQueue[i].Item2.ComputeLayoutParams(width, height);
                    Region test = renderQueue[i].Item1.Occlusion;
                    if (renderQueue[i].Item1.Dirty && test.Subtract(occlusion).Area > 0)
                        return true;
                    else occlusion = occlusion.Add(test);
                }
                return false;
            }
        }

        private ConsoleController(bool resizeListener = true)
        {
            if (resizeListener) EnableResizeListener();
            RegisterListener((w, h) =>
            {
                width = w;
                height = h;
                Draw();
            });

            RegisterListener((w1, h1, w2, h2) => Console.Clear());
        }

        public void AddView(View v, bool redraw = true) => AddView(v, LayoutMeta.Centering(v), redraw);
        public void AddView(View v, LayoutMeta meta, bool redraw = true)
        {
            renderQueue.Add(new Tuple<View, LayoutMeta>(v, meta));
            Draw(false);
        }

        public void CloseTop() => CloseView(renderQueue[renderQueue.Count - 1].Item1);
        public void CloseView(int idx) => CloseView(renderQueue[idx].Item1);
        public void CloseView(View v, bool redraw = true, int maxCloses = -1)
        {
            if (maxCloses == 0) return;

            Region r = new Region();
            bool needsRedraw = false;
            int closed = 0;
            for (int i = renderQueue.Count - 1; i >= 0; --i)
                if (renderQueue[i].Item1.Equals(v))
                {
                    Region test = renderQueue[i].Item1.Occlusion;
                    test.Offset(renderQueue[i].Item2.ComputeLayoutParams(width, height));
                    Region removing = test.Subtract(r);
                    needsRedraw |= removing.Area > 0;

                    Region cmp;
                    for (int j = i - 1; !needsRedraw && j >= 0; --j)
                        needsRedraw |= (cmp = renderQueue[j].Item1.Occlusion).Subtract(removing).Area != cmp.Area;

                    renderQueue.RemoveAt(i);
                    ClearRegion(removing);
                    if (++closed == maxCloses) break;
                }
            if (redraw && needsRedraw) Draw(false);
        }

        public void Draw() => Draw(false);

        // downTo allows for partial rendering updates
        private void Draw(bool ignoreOcclusion, int downTo = 0)
        {
            if (downTo < 0) downTo = 0;
            if (downTo >= renderQueue.Count) return;
            Console.CursorVisible = false;
            byte[] occlusionMap = new byte[(renderQueue.Count / 8) + (renderQueue.Count % 8 != 0 ? 1 : 0)];
            Stack<Tuple<int, int>> layoutParams = new Stack<Tuple<int, int>>();

            Region occlusion = new Region();
            for (int i = renderQueue.Count - 1; i >= downTo; --i)
            {
                Tuple<int, int> lParams = renderQueue[i].Item2.ComputeLayoutParams(width, height);
                if (!ignoreOcclusion)
                {
                    Region test = renderQueue[i].Item1.Occlusion;
                    test.Offset(lParams);
                    if (test.Subtract(occlusion).Area == 0)
                        occlusionMap[i / 8] |= (byte)(1 << (i % 8));
                    else
                    {
                        occlusion = occlusion.Add(test);
                        layoutParams.Push(lParams);
                    }
                } else layoutParams.Push(lParams);
            }

            for (int i = downTo; i < renderQueue.Count; ++i)
                if ((occlusionMap[i / 8] & (1 << (i % 8))) == 0)
                    renderQueue[i].Item1.Draw(layoutParams.Pop());
        }

        public KeyEvent ReadKey(bool redrawOnDirty = true)
        {
            KeyEvent keyInfo = new KeyEvent(Console.ReadKey(true));
            int lowestDirty = -1;
            int count = renderQueue.Count - 1;
            for (int i = count; i >= 0; --i)
                if (renderQueue[i].Item1.HandleKeyEvent(keyInfo, i == count))
                    lowestDirty = i;
            if (redrawOnDirty) Draw(false, lowestDirty);
            return keyInfo;
        }

        public void EnableResizeListener()
        {
            if (cancel != null) return;
            // Set up console window resize listener
            cancel = new CancellationPipe();
            resizeListener = new Task(() => ConsoleResizeListener(cancel)); // Start resize listener asynchronously
            resizeListener.Start();
        }

        public async void DisableResizeListener()
        {
            if (cancel == null) return;
            cancel.Cancel();
            await resizeListener;
        }


        public delegate void WindowChangeListener(int fromWidth, int fromHeight, int toWidth, int toHeight);
        public delegate void WindowChangeCompleteListener(int width, int height);

        private readonly List<WindowChangeListener> changeListeners = new List<WindowChangeListener>();
        private readonly List<WindowChangeCompleteListener> completeListeners = new List<WindowChangeCompleteListener>();

        public void RegisterListener(WindowChangeListener listener) => changeListeners.Add(listener);
        public void RegisterListener(WindowChangeCompleteListener listener) => completeListeners.Add(listener);
        public void UnRegisterListener(WindowChangeListener listener) => changeListeners.RemoveAll(p => p == listener);
        public void UnRegisterListener(WindowChangeCompleteListener listener) => completeListeners.RemoveAll(p => p == listener);

        private void ConsoleResizeListener(CancellationPipe cancel)
        {
            int consoleWidth = Console.WindowWidth;
            int consoleHeight = Console.WindowHeight;
            bool trigger = false;
            int trigger_inc = 0;

            while (!cancel.Cancelled)
            {
                int readWidth = Console.WindowWidth;
                int readHeight = Console.WindowHeight;
                if (readWidth != consoleWidth || readHeight != consoleHeight)
                {
                    trigger = true;
                    foreach (var listener in changeListeners) listener(consoleWidth, consoleHeight, readWidth, readHeight);
                    consoleWidth = readWidth;
                    consoleHeight = readHeight;
                }
                else if (trigger && ++trigger_inc >= 5)
                {
                    foreach (var listener in completeListeners) listener(consoleWidth, consoleHeight);
                    trigger = false;
                }
                System.Threading.Thread.Sleep(50);
            }
        }

        private static void ClearRegion(Region r, ConsoleColor clearColor = ConsoleColor.Black)
        {
            foreach (var rect in r.SubRegions) ClearRegion(rect, clearColor);
        }

        private static void ClearRegion(Rectangle rect, ConsoleColor clearColor = ConsoleColor.Black)
        {
            Console.BackgroundColor = clearColor;
            Console.ForegroundColor = ConsoleColor.White;
            for (int i = rect.Top; i <= rect.Bottom; ++i)
            {
                Console.SetCursorPosition(rect.Left, i);
                for (int j = rect.Right - rect.Left; j > 0; --j)
                    Console.Write(' ');
            }
        }

        private static ViewData DoElementParse(XmlNode el)
        {
            ViewData data = new ViewData(el.LocalName, el.InnerText);

            if (el.Attributes != null)
                foreach (var attr in el.Attributes)
                    if (attr is XmlAttribute)
                        data.attributes[((XmlAttribute)attr).Name] = ((XmlAttribute)attr).Value;

            if (el.ChildNodes != null)
                foreach (var child in el.ChildNodes)
                    if (child is XmlNode) data.nestedData.Add(DoElementParse((XmlNode)child));

            return data;
        }

        private static Dictionary<string, List<Tuple<string, View>>> cache = new Dictionary<string, List<Tuple<string, View>>>();

        public static List<Tuple<string, View>> LoadResourceViews(string name, bool doCache = true)
        {
            if (cache.ContainsKey(name))
                return cache[name];

            PropertyInfo[] properties = typeof(Resources).GetProperties(BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var prop in properties)
                if (prop.Name.Equals(name) && prop.PropertyType.Equals(typeof(string)))
                    return LoadViews((string)prop.GetValue(null), doCache ? name : null);
            throw new SystemException($"Resource { name } could not be located!");
        }
        public static List<Tuple<string, View>> LoadViews(string xml, string cacheID = null)
        {
            if (cacheID != null && cache.ContainsKey(cacheID))
                return cache[cacheID];

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            string ns = doc.FirstChild.NextSibling.Attributes != null ? doc.FirstChild.NextSibling.Attributes.GetNamedItem("xmlns")?.Value ?? "" : "";

            List<Tuple<string, View>> views = new List<Tuple<string, View>>();

            foreach (var child in doc.FirstChild.NextSibling.ChildNodes)
            {
                if (!(child is XmlNode) || child is XmlComment) continue;
                ViewData data = DoElementParse((XmlNode)child);
                View load;
                Type type;
                try { type = Type.GetType(ns + '.' + data.Name, true); }
                catch { type = Type.GetType(data.Name, true); }

                ConstructorInfo info = type.GetConstructor(new Type[] { typeof(ViewData) });

                string id = data.attributes.ContainsKey("id") ? data.attributes["id"] : "";

                load = (View)info.Invoke(new object[] { data });

                views.Add(new Tuple<string, View>(id, load));
            }

            if (cacheID != null) cache[cacheID] = views;

            return views;
        }

        public delegate void Runnable();
        public void Popup(string message, long timeout, ConsoleColor borderColor = ConsoleColor.Blue, Runnable onExpire = null)
        {
            TextBox popup = new TextBox(
                new ViewData("ConsoleForms.TextBox")
                .SetAttribute("padding_left", 2)
                .SetAttribute("padding_right", 2)
                .SetAttribute("padding_top", 1)
                .SetAttribute("padding_bottom", 1)
                .AddNested(new ViewData("Text", message)) // Add message
                )
            {
                BackgroundColor = ConsoleColor.White,
                TextColor = ConsoleColor.Black,
                BorderColor = borderColor,
            };

            AddView(popup, LayoutMeta.Centering(popup));

            new Timer(() => {
                CloseView(popup);
                onExpire?.Invoke();
            }, timeout).Start();

        }
    }

    public sealed class ContextManager
    {
        public Context Current { get; private set; }

        public void LoadContext(Context ctx)
        {
            Current?.OnDestroy();
            Current = ctx;
            Current.OnCreate();
        }

        public bool Update(ConsoleController.KeyEvent keypress, bool hasKeypress = true)
            => Current?.Update(keypress, hasKeypress) == true;
    }

    public abstract class Context
    {
        protected static readonly ConsoleController controller = ConsoleController.singleton;
        protected readonly ReadOnlyCollection<Tuple<string, View>> views;
        protected readonly ContextManager manager;

        public Context(ContextManager manager, string contextName, bool asResource = true)
        {
            this.manager = manager;
            views = new ReadOnlyCollectionBuilder<Tuple<string, View>>(asResource ? ConsoleController.LoadResourceViews(contextName) : ConsoleController.LoadViews(contextName)).ToReadOnlyCollection();
        }

        public virtual bool Update(ConsoleController.KeyEvent keypress, bool hasKeypress = true)
        {
            if (keypress.ValidEvent && keypress.Event.Key == ConsoleKey.Escape) OnDestroy();
            return controller.Dirty;
        }

        public abstract void OnCreate();  // Called when a context is loaded as the primary context of the ConsoleController
        public abstract void OnDestroy(); // Called when a context is unloaded

        protected void RegisterSelectListeners(DialogBox.SelectListener listener, params string[] viewNames)
        {
            foreach(var viewName in viewNames)
            {
                View v = views.GetNamed(viewName);
                if (v != null && v is DialogBox)
                    ((DialogBox)v).RegisterSelectListener(listener);
            }
        }
    }

    public sealed class ViewData
    {
        public delegate string TransformAction(ViewData rawValue);

        public string Name { get; }
        public string InnerText { get; }
        public readonly Dictionary<string, string> attributes = new Dictionary<string, string>();
        public readonly List<ViewData> nestedData = new List<ViewData>();

        public ViewData(string name, string innerText = "")
        {
            Name = (name ?? "").Replace("\r", "");
            InnerText = (innerText ?? "").Replace("\r", "");
        }

        public ViewData Get(string name)
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(name))
                    return data;
            return null;
        }

        public int TextAsInt(int def = default(int)) => int.TryParse(InnerText, out int p) ? p : def;
        public int AttribueAsInt(string name, int def = default(int)) => attributes.ContainsKey(name) && int.TryParse(attributes[name], out int p) ? p : def;
        public bool AttribueAsBool(string name, bool def = default(bool)) => attributes.ContainsKey(name) && bool.TryParse(attributes[name], out bool p) ? p : def;
        public Tuple<string, string>[] CollectSub(string name, TransformAction action = null)
        {
            List<Tuple<string, string>> l = new List<Tuple<string, string>>();
            foreach (var data in nestedData)
                if (data.Name.Equals(name))
                    l.Add(new Tuple<string, string>(data.InnerText, action?.Invoke(data) ?? ""));
            return l.ToArray();
        }
        public string NestedText(string nestedDataName, string def = "")
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(nestedDataName))
                    return data.InnerText;
            return def;
        }
        public int NestedInt(string nestedDataName, int def = default(int))
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(nestedDataName) && int.TryParse(data.InnerText, out int p))
                    return p;
            return def;
        }
        public int NestedAttribute(string nestedName, string attributeName, int def = default(int))
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(nestedName) && data.attributes.ContainsKey(attributeName) && int.TryParse(data.attributes[attributeName], out int p))
                    return p;
            return def;
        }
        public ViewData SetAttribute<T>(string attrName, T value)
        {
            attributes[attrName] = value == null ? "null" : value.ToString();
            return this;
        }
        public ViewData AddNested(ViewData nest)
        {
            nestedData.Add(nest);
            return this;
        }

        public string GetAttribute(string attr, string def = "") => attributes.ContainsKey(attr) ? attributes[attr] : def;
    }

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
            this.gravity = (Gravity) parameters.AttribueAsInt("gravity");
            this.BorderColor = (ConsoleColor)parameters.AttribueAsInt("border", (int)ConsoleColor.Blue);
            this.Border = ' ';
            DrawBorder = true;

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
            for(int i = -1; i<ContentHeight; ++i)
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
            if(back_data.Length!=0 && info.ValidEvent && inFocus && info.Event.Key == ConsoleKey.Escape)
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
                if(close) ConsoleController.singleton.CloseView(this);
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

    public class TextBox : View
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
        public ConsoleColor BackgroundColor { get; set; }
        public ConsoleColor TextColor { get; set; }

        public TextBox(ViewData parameters) : base(parameters)
        {
            //BorderColor = (ConsoleColor) parameters.AttribueAsInt("border", (int)ConsoleColor.Blue);
            BackgroundColor = (ConsoleColor)parameters.AttribueAsInt("color_background", (int)ConsoleColor.White);
            TextColor = (ConsoleColor)parameters.AttribueAsInt("color_text", (int)ConsoleColor.Black);

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
                    if(i<result.Length) result[i] = sub;
                }
                else result[i] = s.Substring(read, Math.Min(s.Length - read, read + max));
                read += valid;
            }
            return result;
        }

        private static bool WillSubSplit(string s, int max) => ((s.Length / max) + (s.Length % max != 0 ? 1 : 0)) > 1 || s.Contains('\n');

        protected override void _Draw(int left, int top)
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

    public class ListView : View
    {


        public ListView(ViewData parameters) : base(parameters)
        {

        }

        public override Region Occlusion => throw new NotImplementedException();

        protected override void _Draw(int left, int top)
        {
            throw new NotImplementedException();
        }
    }

    public class DialogBox : TextBox
    {
        public delegate void SelectListener(DialogBox view, int selectionIndex, string selection);

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

        public DialogBox(ViewData parameters) :
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

        protected override void _Draw(int left, int top)
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

    public class InputTextBox : TextBox
    {
        public enum InputType
        {
            Any,
            AlphaNumeric,
            Integer,
            Decimal,
            Alphabet
        }
        public sealed class InputField
        {
            public const char hide_char = '*';

            public string Label { get; private set; }
            public int MaxLength { get; private set; }
            public bool ShowText { get; set; }
            public string Text { get; set; }
            public int SelectIndex { get; set; }
            public InputType Input { get; set; }
            public ConsoleColor TextColor { get; set; }
            public ConsoleColor BackgroundColor { get; set; }
            public ConsoleColor SelectTextColor { get; set; }
            public ConsoleColor SelectBackgroundColor { get; set; }
            public string InputTypeString
            {
                get
                {
                    switch (Input)
                    {
                        case InputType.Any:
                            return "Any";
                        case InputType.AlphaNumeric:
                            return "AlphaNumeric";
                        case InputType.Integer:
                            return "Integer";
                        case InputType.Decimal:
                            return "Decimal";
                        case InputType.Alphabet:
                            return "Alphabet";
                    }
                    throw new SystemException("Invalid system state detected");
                }

                set
                {
                    switch (value.ToLower())
                    {
                        case "alphanumeric":
                            Input = InputType.AlphaNumeric;
                            break;
                        case "integer":
                            Input = InputType.Integer;
                            break;
                        case "decimal":
                            Input = InputType.Decimal;
                            break;
                        case "alphabet":
                            Input = InputType.Alphabet;
                            break;
                        default:
                            Input = InputType.Any;
                            break;
                    }
                }
            }
            internal int RenderStart { get; set; }

            public InputField(string label, int maxLength)
            {
                TextColor = ConsoleColor.Black;
                BackgroundColor = ConsoleColor.DarkGray;
                SelectTextColor = ConsoleColor.Black;
                SelectBackgroundColor = ConsoleColor.Gray;
                Input = InputType.Any;
                Label = label;
                MaxLength = maxLength;
                Text = "";
            }

            public bool IsValidChar(char c) =>
                (Input == InputType.Any) ||
                (Input == InputType.AlphaNumeric && c.IsAlphaNumeric()) ||
                (Input == InputType.Alphabet && c.IsAlphabetical()) ||
                (Input == InputType.Integer && c.IsNumber()) ||
                (Input == InputType.Decimal && c.IsDecimal());
        }

        public delegate void SubmissionListener(InputTextBox view);
        public delegate bool TextEnteredListener(InputTextBox view, InputField change, ConsoleKeyInfo info);

        public ConsoleColor DefaultBackgroundColor { get; set; }
        public ConsoleColor DefaultTextColor { get; set; }
        public ConsoleColor DefaultSelectBackgroundColor { get; set; }
        public ConsoleColor DefaultSelectTextColor { get; set; }
        public InputField[] Inputs { get; private set; }
        private int selectedField;
        public int SelectedField
        {
            get => selectedField;
            set
            {
                selectedField = value;
                Dirty = true;
            }
        }
        private string[][] splitInputs;

        public SubmissionListener SubmissionsListener { protected get; set; }
        public TextEnteredListener InputListener { protected get; set; }

        public InputTextBox(ViewData parameters) : base(parameters)
        {
            int
                sBC = parameters.AttribueAsInt("textfield_select_color", (int)ConsoleColor.Gray),
                sTC = parameters.AttribueAsInt("text_select_color",      (int)ConsoleColor.Black),
                BC  = parameters.AttribueAsInt("field_noselect_color",   (int)ConsoleColor.DarkGray),
                TC  = parameters.AttribueAsInt("text_noselect_color",    (int)ConsoleColor.Black);

            DefaultBackgroundColor = (ConsoleColor)BC;
            DefaultTextColor = (ConsoleColor)TC;
            DefaultSelectBackgroundColor = (ConsoleColor)sBC;
            DefaultSelectTextColor = (ConsoleColor)sTC;

            List<InputField> fields = new List<InputField>();
            foreach (var data in parameters.nestedData.GetFirst(d => d.Name.Equals("Fields")).nestedData)
                if (!data.Name.Equals("Field")) continue;
                else fields.Add(new InputField(data.InnerText, data.AttribueAsInt("max_length", -1))
                {
                    ShowText = !data.AttribueAsBool("hide", false),
                    Text = data.GetAttribute("default"),
                    InputTypeString = data.GetAttribute("input_type"),
                    TextColor = (ConsoleColor)data.AttribueAsInt("color_text", TC),
                    BackgroundColor = (ConsoleColor)data.AttribueAsInt("color_background", BC),
                    SelectTextColor = (ConsoleColor)data.AttribueAsInt("color_text_select", sTC),
                    SelectBackgroundColor = (ConsoleColor)data.AttribueAsInt("color_background_select", sBC)
                });

            Inputs = fields.ToArray();

            int computedSize = 0;
            splitInputs = new string[Inputs.Length][];
            for(int i = 0; i< Inputs.Length; ++i)
            {
                splitInputs[i] = ComputeTextDimensions(Inputs[i].Label.Split(' '));
                computedSize += splitInputs[i].Length;
            }
            ContentHeight += computedSize + Inputs.Length * 2;
        }

        protected override void _Draw(int left, int top)
        {
            DrawEmptyPadding(left, ref top, padding.Top());
            DrawContent(left, ref top);
            DrawInputFields(left, ref top, 1);
            DrawEmptyPadding(left, ref top, padding.Bottom());
        }

        protected void DrawInputFields(int left, ref int top, int spaceHeight)
        {
            int pl = padding.Left(), pr = padding.Right();

            for (int j = 0; j< Inputs.Length; ++j)
            {
                DrawEmptyPadding(left, ref top, spaceHeight);
                
                for (int i = 0; i < splitInputs[j].Length; ++i)
                {
                    Console.SetCursorPosition(left, top++);
                    Console.BackgroundColor = BackgroundColor;
                    Console.Write(Filler(' ', pl) + splitInputs[j][i] + Filler(' ', MaxWidth - splitInputs[j][i].Length) + Filler(' ', pr));
                }
                Console.SetCursorPosition(left, top++);

                // Draw padding
                Console.BackgroundColor = BackgroundColor;
                Console.Write(Filler(' ', pl));

                // Draw field
                Console.BackgroundColor = j == selectedField ? Inputs[j].SelectBackgroundColor : Inputs[j].BackgroundColor;
                Console.ForegroundColor = j == selectedField ? Inputs[j].SelectTextColor : Inputs[j].TextColor;
                Console.Write(Inputs[j].ShowText ? Inputs[j].Text.Substring(Inputs[j].RenderStart, Inputs[j].SelectIndex - Inputs[j].RenderStart) : Filler('*', Inputs[j].SelectIndex - Inputs[j].RenderStart));
                if(j == selectedField) Console.BackgroundColor = ConsoleColor.DarkGray;
                Console.Write(Inputs[j].SelectIndex < Inputs[j].Text.Length ? Inputs[j].ShowText ? Inputs[j].Text[Inputs[j].SelectIndex] : '*' : ' ');
                if (j == selectedField) Console.BackgroundColor = Inputs[j].SelectBackgroundColor;
                int drawn = 0;
                if(Inputs[j].SelectIndex < Inputs[j].Text.Length)
                    Console.Write(
                        Inputs[j].ShowText ?
                        Inputs[j].Text.Substring(Inputs[j].SelectIndex + 1, drawn = Math.Min(maxWidth + Inputs[j].SelectIndex - Inputs[j].RenderStart - 1, Inputs[j].Text.Length - Inputs[j].SelectIndex - 1)) :
                        Filler('*', drawn = Math.Min(maxWidth + Inputs[j].SelectIndex - Inputs[j].RenderStart - 1, Inputs[j].Text.Length - Inputs[j].SelectIndex - 1))
                        );
                Console.Write(Filler(' ', maxWidth - 1 - drawn - Inputs[j].SelectIndex + Inputs[j].RenderStart));
                Console.ForegroundColor = ConsoleColor.Black;

                // Draw padding
                Console.BackgroundColor = BackgroundColor;
                Console.Write(Filler(' ', pr));
                
            }
        }

        public override bool HandleKeyEvent(ConsoleController.KeyEvent evt, bool inFocus)
        {
            bool changed = base.HandleKeyEvent(evt, inFocus);
            ConsoleKeyInfo info = evt.Event;
            if (!evt.ValidEvent || !inFocus || Inputs.Length == 0) return changed;
            switch (info.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (Inputs[selectedField].SelectIndex > 0)
                    {
                        if (Inputs[selectedField].RenderStart == Inputs[selectedField].SelectIndex--) --Inputs[selectedField].RenderStart;
                    }
                    else return changed;
                    break;
                case ConsoleKey.RightArrow:
                    if (Inputs[selectedField].SelectIndex < Inputs[selectedField].Text.Length)
                    {
                        if (++Inputs[selectedField].SelectIndex - Inputs[selectedField].RenderStart == maxWidth) ++Inputs[selectedField].RenderStart;
                    }
                    else return changed;
                    break;
                case ConsoleKey.Tab:
                case ConsoleKey.DownArrow:
                    if (selectedField < Inputs.Length - 1) ++selectedField;
                    else return changed;
                    break;
                case ConsoleKey.UpArrow:
                    if (selectedField > 0) --selectedField;
                    else return changed;
                    break;
                case ConsoleKey.Backspace:
                    if (Inputs[selectedField].SelectIndex > 0)
                    {
                        if (InputListener?.Invoke(this, Inputs[selectedField], info)==false) break;
                        string text = Inputs[selectedField].Text;
                        Inputs[selectedField].Text = text.Substring(0, Inputs[selectedField].SelectIndex - 1);
                        if(Inputs[selectedField].SelectIndex < text.Length) Inputs[selectedField].Text += text.Substring(Inputs[selectedField].SelectIndex);
                        if (Inputs[selectedField].RenderStart == Inputs[selectedField].SelectIndex--) --Inputs[selectedField].RenderStart;
                    }
                    else return changed;
                    break;
                case ConsoleKey.Delete:
                    if (Inputs[selectedField].SelectIndex < Inputs[selectedField].Text.Length)
                    {
                        if (InputListener?.Invoke(this, Inputs[selectedField], info) == false) break;
                        string text = Inputs[selectedField].Text;
                        Inputs[selectedField].Text = text.Substring(0, Inputs[selectedField].SelectIndex);
                        if (Inputs[selectedField].SelectIndex + 1 < text.Length) Inputs[selectedField].Text += text.Substring(Inputs[selectedField].SelectIndex + 1);
                    }
                    else return changed;
                    break;
                case ConsoleKey.Enter:
                    SubmissionsListener?.Invoke(this);
                    return changed;
                default:
                    if (info.KeyChar != 0 && info.KeyChar!='\b' && info.KeyChar!='\r' && (Inputs[selectedField].Text.Length < Inputs[selectedField].MaxLength || Inputs[selectedField].MaxLength < 0) && Inputs[selectedField].IsValidChar(info.KeyChar))
                    {
                        if (InputListener?.Invoke(this, Inputs[selectedField], info) == false) break;
                        Inputs[selectedField].Text = Inputs[selectedField].Text.Substring(0, Inputs[selectedField].SelectIndex) + info.KeyChar + Inputs[selectedField].Text.Substring(Inputs[selectedField].SelectIndex);
                        if (++Inputs[selectedField].SelectIndex - Inputs[selectedField].RenderStart == maxWidth) ++Inputs[selectedField].RenderStart;
                    } else return changed;
                    break;
            }
            return true;
        }
    }

    public class LayoutParameterException : SystemException
    {
        public LayoutParameterException() { }
        public LayoutParameterException(string message) : base(message) { }
        public LayoutParameterException(string message, Exception innerException) : base(message, innerException) { }
        protected LayoutParameterException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }


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

    public class Region
    {
        protected readonly List<Rectangle> region = new List<Rectangle>();

        public int Area
        {
            get
            {
                int total = 0;
                foreach(var rect in region)
                    total += (rect.Left - rect.Right) * (rect.Top - rect.Bottom);
                return total;
            }
        }
        public Rectangle[] SubRegions => region.ToArray();

        public Region(params Rectangle[] rectangles)
        {
            if (rectangles.Length > 0) region.Add(rectangles[0]);
            for (int i = 1; i < rectangles.Length; ++i) Add(rectangles[i]);
        }

        public Region(List<Rectangle> region) => this.region.AddRange(region);
        public Region(Region r) => this.region.AddRange(r.region);

        public Region Add(Rectangle rect)
        {
            Region r = new Region(region);
            r.IAdd(rect);
            return r;
        }

        protected void IAdd(Rectangle rect)
        {
            List<Rectangle> recompute = new List<Rectangle>();
            foreach (var rectangle in region) recompute.AddRange(rectangle.Subtract(rect));
            recompute.Add(rect);
            region.Clear();
            region.AddRange(recompute);
        }

        public Region Add(Region region)
        {
            Region r = new Region(this);
            foreach (var rectangle in region.region) r.IAdd(rectangle);
            return r;
        }

        public Region Subtract(Rectangle rect)
        {
            Region r = new Region(region);
            r.ISubtract(rect);
            return r;
        }

        protected void ISubtract(Rectangle rect)
        {
            List<Rectangle> recompute = new List<Rectangle>();
            foreach (var rectangle in region) recompute.AddRange(rectangle.Subtract(rect));
            region.Clear();
            region.AddRange(recompute);
        }

        public Region Subtract(Region region)
        {
            Region r = new Region(this);
            foreach (var rectangle in region.region) r.ISubtract(rectangle);
            return r;
        }

        public void Offset(Tuple<int, int> xy) => Offset(xy.Item1, xy.Item2);
        public void Offset(int x, int y)
        {
            foreach (var rect in region) rect.Offset(x, y);
        }
    }

    public class Rectangle
    {
        public int Top { get; private set; }
        public int Bottom { get; private set; }
        public int Left { get; private set; }
        public int Right { get; private set; }
        public Rectangle(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public bool Intersects(Rectangle rect) => ((Left < rect.Right && Right >= rect.Left) || (Left <= rect.Right && Right > rect.Left)) && ((Top > rect.Bottom && Bottom <= rect.Top) || (Top >= rect.Bottom && Bottom < rect.Top));
        public bool Occludes(Rectangle rect) => Top >= rect.Top && Right >= rect.Right && Left >= rect.Left && Bottom >= rect.Bottom;
        public Rectangle GetIntersecting(Rectangle rect)
            => Intersects(rect) ?
            new Rectangle(
                Left < rect.Right ? Left : rect.Left,
                Bottom < rect.Top ? rect.Top : Top,
                Left < rect.Right ? rect.Right : Right,
                Bottom < rect.Top ? Bottom : rect.Bottom
                ) :
            null;

        public Rectangle[] Subtract(Rectangle rect)
        {
            Rectangle intersect = GetIntersecting(rect);
            if (intersect == null || rect.Occludes(this)) return new Rectangle[0];
            Rectangle[] components = new Rectangle[(intersect.Left > Left ? 1 : 0) + (intersect.Right < Right ? 1 : 0) + (intersect.Top > Top ? 1 : 0) + (intersect.Bottom < Bottom ? 1 : 0)];
            int rectangles = 0;

            if(intersect.Left > Left)
                components[rectangles++] = new Rectangle(Left, Math.Max(intersect.Top, Top), intersect.Left, Math.Min(intersect.Bottom, Bottom));
            if (intersect.Right < Right)
                components[rectangles++] = new Rectangle(intersect.Right, Math.Max(intersect.Top, Top), Left, Math.Min(intersect.Bottom, Bottom));
            if (intersect.Top > Top)
                components[rectangles++] = new Rectangle(Math.Min(Left, intersect.Left), Top, Math.Max(Right, intersect.Right), intersect.Top);
            if (intersect.Bottom < Bottom)
                components[rectangles] = new Rectangle(Math.Min(Left, intersect.Left), intersect.Bottom, Math.Max(Right, intersect.Right), Bottom);

            return components;
        }

        public void Offset(Tuple<int, int> xy) => Offset(xy.Item1, xy.Item2);
        public void Offset(int x, int y)
        {
            Left += x;
            Bottom += y;
            Right += x;
            Top += y;
        }
    }

    public static class SpaceMaths
    {
        public static Tuple<int, int> CenterPad(int maxLength, int contentLength)
        {
            int pad = maxLength - contentLength;
            return new Tuple<int, int>(pad / 2, pad - (pad / 2));
        }
    }

    public sealed class CancellationPipe
    {
        private bool cancel = false;
        public bool Cancelled
        {
            get => cancel;
            set => cancel |= value;
        }

        // Redundant
        public void Cancel() => Cancelled = true;
    }

    public sealed class Timer
    {
        public delegate void Runnable();
        private readonly long millis;
        private readonly Task timer;

        public bool Expired => timer.Status != TaskStatus.Running;

        public Timer(Runnable onExpire, long millis, int resolution = 100)
        {
            this.millis = CurrentTimeMillis() + millis;
            timer = new Task(() =>
            {
                while (CurrentTimeMillis() < this.millis) System.Threading.Thread.Sleep(resolution);
                onExpire();
            });
        }
        public void Start() => timer.Start();
        public TaskAwaiter GetAwaiter() => timer.GetAwaiter();

        private static long CurrentTimeMillis() => DateTime.Now.Ticks / 10000;
    }
}
