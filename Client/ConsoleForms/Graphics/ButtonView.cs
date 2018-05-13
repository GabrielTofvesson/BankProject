using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.ConsoleForms.Parameters;
using Client.ConsoleForms.Events;

namespace Client.ConsoleForms.Graphics
{
    public class ButtonView : TextView, ISubmissionListener
    {
        protected SubmissionEvent evt;

        public ButtonView(ViewData parameters, LangManager lang) : base(parameters, lang)
        {
        }

        public override bool HandleKeyEvent(ConsoleController.KeyEvent info, bool inFocus)
        {
            bool b = inFocus && info.ValidEvent && info.Event.Key == ConsoleKey.Enter;
            if (b) evt?.Invoke(this);
            return b;
        }

        public void SetEvent(SubmissionEvent listener) => evt = listener;
    }
}
