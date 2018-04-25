using Client.ConsoleForms;
using Client.ConsoleForms.Graphics;
using ConsoleForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;

namespace Client
{
    public sealed class WelcomeContext : Context
    {
        private readonly BankNetInteractor interactor;
        private long token;
        private Promise promise;
        private bool forceDestroy = true;

        public WelcomeContext(ContextManager manager, BankNetInteractor connection) : base(manager, "Setup", "Common")
        {
            this.interactor = connection;

            // Prepare events and stuff

            // Just close when anything is selected and "submitted"
            RegisterSelectListeners((s, i, v) => controller.CloseView(s), "DuplicateAccountError", "EmptyFieldError", "IPError", "PortError", "AuthError", "PasswordMismatchError");


            GetView<InputView>("Login").SubmissionsListener = i =>
            {
                bool success = true;

                foreach (var input in i.Inputs)
                    if (input.Text.Length == 0)
                    {
                        success = false;
                        input.SelectBackgroundColor = ConsoleColor.Red;
                        input.BackgroundColor = ConsoleColor.DarkRed;
                    }
                

                if (success)
                {
                    // Authenticate against server here
                    Show("AuthWait");
                    Task<Promise> prom = interactor.Authenticate(i.Inputs[0].Text, i.Inputs[1].Text);
                    if(!prom.IsCompleted) prom.RunSynchronously();
                    promise = prom.Result;
                    promise.Subscribe =
                    response =>
                    {
                        Hide("AuthWait");
                        if (response.Value.Equals("ERROR"))
                            Show("AuthError");
                        else
                        {
                            forceDestroy = false;
                            manager.LoadContext(new SessionContext(manager, interactor, response.Value));
                        }
                    };
                }
                else Show("EmptyFieldError");
            };

            // For a smooth effect
            GetView<InputView>("Login").InputListener = (v, c, i) =>
            {
                c.BackgroundColor = v.DefaultBackgroundColor;
                c.SelectBackgroundColor = v.DefaultSelectBackgroundColor;
                return true;
            };

            GetView<InputView>("Register").SubmissionsListener = i =>
            {
                bool success = true, mismatch = false;

                foreach (var input in i.Inputs)
                {
                    if (input.Text.Length == 0)
                    {
                        success = false;
                        input.SelectBackgroundColor = ConsoleColor.Red;
                        input.BackgroundColor = ConsoleColor.DarkRed;
                    }
                }

                mismatch = !i.Inputs[1].Text.Equals(i.Inputs[2].Text);
                if (success && !mismatch)
                {
                    void a()
                    {
                        Show("RegWait");
                        promise = Promise.AwaitPromise(interactor.Register(i.Inputs[0].Text, i.Inputs[1].Text));
                        promise.Subscribe =
                        response =>
                        {
                            Hide("RegWait");
                            if (response.Value.Equals("ERROR"))
                                Show("DuplicateAccountError");
                            else
                            {
                                forceDestroy = false;
                                manager.LoadContext(new SessionContext(manager, interactor, response.Value));
                            }
                        };
                    }

                    if (i.Inputs[1].Text.Length < 5 || i.Inputs[1].Text.StartsWith("asdfasdf") || i.Inputs[1].Text.StartsWith("asdf1234"))
                    {
                        var warning = GetView<DialogView>("WeakPasswordWarning");
                        warning.RegisterSelectListener((wrn, idx, sel) =>
                        {
                            Hide(warning);
                            if (idx == 0) a();
                        });
                        Show(warning);
                    }
                    else a();
                }
                else if (mismatch) Show("PasswordMismatchError");
                else Show("EmptyFieldError");
            };

            ((InputView)views.GetNamed("Register")).InputListener = (v, c, i) =>
            {
                c.BackgroundColor = v.DefaultBackgroundColor;
                c.SelectBackgroundColor = v.DefaultSelectBackgroundColor;
                return true;
            };
        }

        public override void OnCreate()
        {
            token = interactor.RegisterListener((c, s) =>
            {
                if(!s) controller.Popup("The connection to the server was severed! ", 4500, ConsoleColor.DarkRed, () => manager.LoadContext(new NetContext(manager)));
            });

            // Add the initial view
            Show("WelcomeScreen");
        }

        public override void OnDestroy()
        {
            GetView<InputView>("Register").SelectedField = 0;
            foreach (var v in GetView<InputView>("Register").Inputs)
            {
                v.Text = "";
                v.SelectIndex = 0;
                v.RenderStart = 0;
            }

            ((InputView)views.GetNamed("Login")).SelectedField = 0;
            foreach (var v in GetView<InputView>("Login").Inputs)
            {
                v.Text = "";
                v.SelectIndex = 0;
                v.RenderStart = 0;
            }

            // Close views
            HideAll();

            // Unsubscribe from events
            if (promise != null && !promise.HasValue) promise.Subscribe = null;

            // Stop listening
            interactor.UnregisterListener(token);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            if (forceDestroy) interactor.CancelAll();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }
}
