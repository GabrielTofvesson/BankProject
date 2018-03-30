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

        public SessionContext(ContextManager manager, BankNetInteractor interactor, string sessionID) : base(manager, "Session")
        {
            this.interactor = interactor;
            this.sessionID = sessionID;

            ((DialogBox)views.GetNamed("Success")).RegisterSelectListener((v, i, s) =>
            {
                interactor.Logout(sessionID);
                manager.LoadContext(new NetContext(manager));
            });
        }

        public override void OnCreate()
        {
            controller.AddView(views.GetNamed("Success"));
        }

        public override void OnDestroy()
        {
            controller.CloseView(views.GetNamed("Success"));
            interactor.Disconnect();
        }
    }
}
