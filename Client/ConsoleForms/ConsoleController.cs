using Client.ConsoleForms.Graphics;
using Client.ConsoleForms.Parameters;
using Client.Properties;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;

namespace Client.ConsoleForms
{
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
                // Corrective resizing to prevent rendering issues
                if (w < 20 || h < 20)
                {
                    Console.SetWindowSize(Math.Max(w, 60), Math.Max(h, 40));
                    return;
                }
                width = w;
                height = h;
                Draw();
            });

            RegisterListener((w1, h1, w2, h2) =>
            {
                // Corrective resizing to prevent rendering issues
                if (w2 < 20 || h2 < 20)
                    Console.SetWindowSize(Math.Max(w2, 60), Math.Max(h2, 40));
                Console.BackgroundColor = ConsoleColor.Black;
                Console.Clear();
            });
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
                }
                else layoutParams.Push(lParams);
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

        public static void ClearRegion(Region r, ConsoleColor clearColor = ConsoleColor.Black)
        {
            foreach (var rect in r.SubRegions) ClearRegion(rect, clearColor);
        }

        public static void ClearRegion(Rectangle rect, ConsoleColor clearColor = ConsoleColor.Black)
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

        private static ViewData DoElementParse(XmlNode el, LangManager lang)
        {
            ViewData data = new ViewData(el.LocalName, lang.MapIfExists(el.InnerText));

            if (el.Attributes != null)
                foreach (var attr in el.Attributes)
                    if (attr is XmlAttribute)
                        data.attributes[((XmlAttribute)attr).Name] = ((XmlAttribute)attr).Value;

            if (el.ChildNodes != null)
                foreach (var child in el.ChildNodes)
                    if (child is XmlNode) data.nestedData.Add(DoElementParse((XmlNode)child, lang));

            return data;
        }

        private static Dictionary<string, List<Tuple<string, View>>> cache = new Dictionary<string, List<Tuple<string, View>>>();

        public static List<Tuple<string, View>> LoadResourceViews(string name, LangManager lang, bool doCache = true)
        {
            if (cache.ContainsKey(name))
                return cache[name];

            PropertyInfo[] properties = typeof(Resources).GetProperties(BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var prop in properties)
                if (prop.Name.Equals(name) && prop.PropertyType.Equals(typeof(string)))
                    return LoadViews((string)prop.GetValue(null), lang, doCache ? name : null);
            throw new SystemException($"Resource { name } could not be located!");
        }
        public static List<Tuple<string, View>> LoadViews(string xml, LangManager lang, string cacheID = null)
        {
            if (cacheID != null && cache.ContainsKey(cacheID))
                return cache[cacheID];

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            string ns = doc.FirstChild.NextSibling.Attributes != null ? doc.FirstChild.NextSibling.Attributes.GetNamedItem("xmlns")?.Value ?? "" : "";

            List<Tuple<string, View>> views = new List<Tuple<string, View>>();

            foreach (var child in doc.FirstChild.NextSibling.ChildNodes)
                if (!(child is XmlNode) || child is XmlComment) continue;
                else views.Add(LoadView(ns, DoElementParse((XmlNode)child, lang), lang));

            if (cacheID != null) cache[cacheID] = views;

            return views;
        }

        public static Tuple<string, View> LoadView(string ns, ViewData data, LangManager lang)
        {
            Type type;
            try { type = Type.GetType(ns + '.' + data.Name, true); }
            catch { type = Type.GetType(data.Name, true); }
            
            ConstructorInfo info = type.GetConstructor(new Type[] { typeof(ViewData), typeof(LangManager) });

            string id = data.attributes.ContainsKey("id") ? data.attributes["id"] : "";
            data.attributes["xmlns"] = ns;

            return new Tuple<string, View>(id, (View)info.Invoke(new object[] { data, lang }));
        }

        public delegate void Runnable();
        public void Popup(string message, long timeout, ConsoleColor borderColor = ConsoleColor.Blue, Runnable onExpire = null)
        {
            TextView popup = new TextView(
                new ViewData("ConsoleForms.TextBox")
                .SetAttribute("padding_left", 2)
                .SetAttribute("padding_right", 2)
                .SetAttribute("padding_top", 1)
                .SetAttribute("padding_bottom", 1)
                .AddNested(new ViewData("Text", message)), // Add message
                LangManager.NO_LANG
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
}
