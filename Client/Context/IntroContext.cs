using System;
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
                Hide(v);
                if (i == 1) onComplete();
                else Show("describe1");
            });

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

        public override void OnCreate() => Show("welcome");
        public override void OnDestroy() { }
    }
}
