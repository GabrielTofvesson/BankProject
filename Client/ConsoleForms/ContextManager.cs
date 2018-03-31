using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms
{
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
}
