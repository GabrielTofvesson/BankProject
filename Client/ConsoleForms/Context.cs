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

        protected void RegisterSelectListeners(DialogView.SelectListener listener, params string[] viewNames)
        {
            foreach (var viewName in viewNames)
            {
                View v = views.GetNamed(viewName);
                if (v != null && v is DialogView)
                    ((DialogView)v).RegisterSelectListener(listener);
            }
        }
    }
}
