using Client.ConsoleForms.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;

namespace Client.ConsoleForms
{
    public abstract class Context
    {
        protected static readonly ConsoleController controller = ConsoleController.singleton;
        protected readonly ReadOnlyCollection<Tuple<string, View>> views;
        protected readonly ContextManager manager;

        private delegate List<Tuple<string, View>> ViewLoader(string data, LangManager lang);
        public Context(ContextManager manager, params string[] contextNames) : this(manager, true, contextNames) { }
        public Context(ContextManager manager, bool asResource, params string[] contextNames)
        {
            this.manager = manager;

            ViewLoader loader;
            if (asResource) loader = (d, m) => ConsoleController.LoadResourceViews(d, m);
            else loader = (d, m) => ConsoleController.LoadViews(d, m);

            List<Tuple<string, View>> l = new List<Tuple<string, View>>();
            foreach(var contextName in contextNames)
                foreach (var viewPair in loader(contextName, manager.I18n))
                    if (l.GetNamed(viewPair.Item1) != null) throw new SystemException($"View with id=\"{viewPair.Item1}\" has already been loaded!");
                    else l.Add(viewPair);
            
            views = new ReadOnlyCollectionBuilder<Tuple<string, View>>(l).ToReadOnlyCollection();
        }

        public virtual bool Update(ConsoleController.KeyEvent keypress, bool hasKeypress = true)
        {
            if (keypress.ValidEvent && keypress.Event.Key == ConsoleKey.Escape) OnDestroy();
            return controller.Dirty;
        }

        public abstract void OnCreate();  // Called when a context is loaded as the primary context of the ConsoleController
        public abstract void OnDestroy(); // Called when a context is unloaded

        protected void RegisterSelectListeners(DialogView.SelectListener listener, params string[] viewNames)
        {
            foreach (var viewName in viewNames)
            {
                View v = views.GetNamed(viewName);
                if (v != null && v is DialogView)
                    ((DialogView)v).RegisterSelectListener(listener);
            }
        }
        protected void Show(View v) => controller.AddView(v);
        protected void Show(string viewID) => controller.AddView(views.GetNamed(viewID));
        protected T GetView<T>(string viewID) where T : View => (T) views.GetNamed(viewID);
        protected View GetView(string viewID) => views.GetNamed(viewID);
        protected void Hide(string viewID) => controller.CloseView(views.GetNamed(viewID));
        protected void Hide(View v) => controller.CloseView(v);
        protected void HideAll()
        {
            foreach (var viewEntry in views)
                Hide(viewEntry.Item2);
        }
    }
}
