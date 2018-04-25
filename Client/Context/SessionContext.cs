using Client.ConsoleForms;
using Client.ConsoleForms.Graphics;
using Client.Properties;
using ConsoleForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;
using Tofvesson.Common;
using Tofvesson.Crypto;

namespace Client
{
    public sealed class SessionContext : Context
    {
        private readonly BankNetInteractor interactor;
        private readonly string sessionID;

        public SessionContext(ContextManager manager, BankNetInteractor interactor, string sessionID) : base(manager, "Session", "Common")
        {
            this.interactor = interactor;
            this.sessionID = sessionID;

            GetView<DialogView>("Success").RegisterSelectListener((v, i, s) =>
            {
                interactor.Logout(sessionID);
                manager.LoadContext(new NetContext(manager));
            });
        }

        public override void OnCreate() => Show("menu_options");

        public override void OnDestroy()
        {
            controller.CloseView(views.GetNamed("Success"));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            interactor.CancelAll();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }
}
