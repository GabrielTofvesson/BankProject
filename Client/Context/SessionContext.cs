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
    public sealed class SessionContext : Context
    {
        private readonly BankNetInteractor interactor;
        private readonly string sessionID;

        public SessionContext(ContextManager manager, BankNetInteractor interactor, string sessionID) : base(manager, "Session", "Common")
        {
            this.interactor = interactor;
            this.sessionID = sessionID;

            ((DialogView)views.GetNamed("Success")).RegisterSelectListener((v, i, s) =>
            {
                interactor.Logout(sessionID);
                manager.LoadContext(new NetContext(manager));
            });
        }

        public override void OnCreate()
        {
            //controller.AddView(views.GetNamed("Success"));
            controller.AddView(views.GetNamed("menu_options"));
        }

        public override void OnDestroy()
        {
            controller.CloseView(views.GetNamed("Success"));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            interactor.Disconnect();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }
}
