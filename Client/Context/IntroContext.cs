using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.ConsoleForms;
using Client.ConsoleForms.Graphics;

namespace Client
{
    public sealed class IntroContext : Context
    {
        public IntroContext(ContextManager manager, Action onComplete) : base(manager, "Intro", "Common")
        {
            GetView<DialogView>("welcome").RegisterSelectListener((v, i, s) =>
            {
                if (i == 1)
                {
                    Hide(v);
                    onComplete();
                }
                else
                {
                    Hide(v);
                    Show("describe1");
                }
            });

            GetView<DialogView>("describe1").RegisterSelectListener((v, i, s) =>
            {
                if (i == 1) v.TriggerKeyEvent(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
                else
                {
                    Hide(v);
                    Show("describe2");
                }
            });

            GetView<DialogView>("describe2").RegisterSelectListener((v, i, s) =>
            {
                if (i == 1) v.TriggerKeyEvent(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
                else
                {
                    Hide(v);
                    Show("describe3");
                }
            });

            GetView<InputView>("describe3").SubmissionsListener = v =>
            {
                Hide(v);
                Show("describe4");
            };

            GetView<InputView>("describe4").SubmissionsListener = v =>
            {
                Hide(v);
                Show("describe4_1");
            };

            GetView<InputView>("describe4_1").SubmissionsListener = v =>
            {
                Hide(v);
                Show("describe5");
            };

            GetView<DialogView>("describe5").RegisterSelectListener((v, i, s) =>
            {
                Hide(v);
                Show("describe4_1");
            });

            GetView<DialogView>("describe5").OnBackEvent = v =>
            {
                Hide(v);
                onComplete();
            };
        }

        public override void OnCreate()
        {
            Show("welcome");
        }

        public override void OnDestroy()
        {

        }

        // Graphics update trigger
        public override bool Update(ConsoleController.KeyEvent keypress, bool hasKeypress = true)
        {

            // Return: whether or not to redraw graphics
            return base.Update(keypress, hasKeypress);
        }
    }
}
