using Client.ConsoleForms.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms.Events
{
    public delegate void TextInputEvent(ConsoleKeyInfo info, View listener);
    public interface ITextInputEventListener
    {
        void SetEvent(TextInputEvent listener);
    }
}
