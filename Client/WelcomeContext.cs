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

        public WelcomeContext(ContextManager manager, BankNetInteractor connection) : base(manager, "Setup")
        {
            this.interactor = connection;

            // Prepare events and stuff

            // Just close when anything is selected and "submitted"
            RegisterSelectListeners((s, i, v) => controller.CloseView(s), "DuplicateAccountError", "EmptyFieldError", "IPError", "PortError", "AuthError", "PasswordMismatchError");


            ((InputTextBox)views.GetNamed("Login")).SubmissionsListener = i =>
            {
                bool success = true;

                foreach (var input in i.Inputs)
                {
                    if (input.Text.Length == 0)
                    {
                        success = false;
                        input.SelectBackgroundColor = ConsoleColor.Red;
                        input.BackgroundColor = ConsoleColor.DarkRed;
                    }
                }

                if (success)
                {
                    // Authenticate against server here
                    controller.AddView(views.GetNamed("AuthWait"));
                    promise = interactor.Authenticate(i.Inputs[0].Text, i.Inputs[1].Text);
                    promise.Subscribe =
                    response =>
                    {
                        controller.CloseView(views.GetNamed("AuthWait"));
                        if (response.Value.Equals("ERROR"))
                            controller.AddView(views.GetNamed("AuthError"));
                        else
                        {
                            forceDestroy = false;
                            manager.LoadContext(new SessionContext(manager, interactor, response.Value));
                        }
                    };
                }
                else controller.AddView(views.GetNamed("EmptyFieldError"));
            };

            // For a smooth effect
            ((InputTextBox)views.GetNamed("Login")).InputListener = (v, c, i) =>
            {
                c.BackgroundColor = v.DefaultBackgroundColor;
                c.SelectBackgroundColor = v.DefaultSelectBackgroundColor;
                return true;
            };

            ((InputTextBox)views.GetNamed("Register")).SubmissionsListener = i =>
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
                        controller.AddView(views.GetNamed("RegWait"));
                        promise = interactor.Register(i.Inputs[0].Text, i.Inputs[1].Text);
                        promise.Subscribe =
                        response =>
                        {
                            controller.CloseView(views.GetNamed("RegWait"));
                            if (response.Value.Equals("ERROR"))
                                controller.AddView(views.GetNamed("DuplicateAccountError"));
                            else
                            {
                                forceDestroy = false;
                                manager.LoadContext(new SessionContext(manager, interactor, response.Value));
                            }
                        };
                    }

                    if (i.Inputs[1].Text.Length < 5 || i.Inputs[1].Text.StartsWith("asdfasdf") || i.Inputs[1].Text.StartsWith("asdf1234"))
                    {
                        var warning = (DialogBox)views.GetNamed("WeakPasswordWarning");
                        warning.RegisterSelectListener((wrn, idx, sel) =>
                        {
                            controller.CloseView(warning);
                            if (idx == 0) a();
                        });
                        controller.AddView(warning);
                    }
                    else a();
                }
                else if (mismatch) controller.AddView(views.GetNamed("PasswordMismatchError"));
                else controller.AddView(views.GetNamed("EmptyFieldError"));
            };

            ((InputTextBox)views.GetNamed("Register")).InputListener = (v, c, i) =>
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
            controller.AddView(views.GetNamed("WelcomeScreen"));
        }

        public override void OnDestroy()
        {
            // TODO: Save state


            // Close views
            foreach (var view in views)
                controller.CloseView(view.Item2);

            // Unsubscribe from events
            if (promise != null && !promise.HasValue) promise.Subscribe = null;

            // Stop listening
            interactor.UnregisterListener(token);

            if (forceDestroy) interactor.Disconnect();
        }
    }
}
