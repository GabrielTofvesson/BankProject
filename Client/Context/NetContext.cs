using Client.ConsoleForms;
using ConsoleForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;
using Client.ConsoleForms.Graphics;

namespace Client
{
    public class NetContext : Context
    {
        public NetContext(ContextManager manager) : base(manager, "Networking")
        {
            // Just close when anything is selected and "submitted"
            RegisterSelectListeners((s, i, v) => controller.CloseView(s), "EmptyFieldError", "IPError", "PortError", "ConnectionError");

            ((InputView)views.GetNamed("NetConnect")).SubmissionsListener = i =>
            {
                bool
                    ip = ParseIP(i.Inputs[0].Text) != null,
                    port = short.TryParse(i.Inputs[1].Text, out short prt) && prt > 0;


                if (ip && port)
                {
                    // Connect to server here
                    BankNetInteractor ita = new BankNetInteractor(i.Inputs[0].Text, prt, false); // Don't do identity check for now
                    try
                    {
                        var t = ita.Connect();
                        while (!t.IsCompleted)
                            if (t.IsCanceled || t.IsFaulted)
                            {
                                controller.AddView(views.GetNamed("ConnectError"));
                                return;
                            }
                    }
                    catch
                    {
                        controller.AddView(views.GetNamed("ConnectionError"));
                        return;
                    }
                    manager.LoadContext(new WelcomeContext(manager, ita));
                }
                else if (i.Inputs[0].Text.Length == 0 || i.Inputs[1].Text.Length == 0) controller.AddView(views.GetNamed("EmptyFieldError"));
                else if (!ip) controller.AddView(views.GetNamed("IPError"));
                else controller.AddView(views.GetNamed("PortError"));
            };
        }

        public override void OnCreate()
        {
            controller.AddView(views.GetNamed("NetConnect"));
        }

        public override void OnDestroy()
        {
            foreach (var view in views)
                controller.CloseView(view.Item2);
        }


        //int gtrack = 0;
        public override bool Update(ConsoleController.KeyEvent keypress, bool hasKeypress = true)
        {
            /*
            var connectBox = (TextBox)views.GetNamed("NetConnect");
            if (++gtrack == 10)
            {
                connectBox.BorderColor = (ConsoleColor)((int)(connectBox.BorderColor + 1) % 16);
                gtrack = 0;
            }
            
            connectBox.Dirty = true;
            */
            return base.Update(keypress, hasKeypress);
        }

        private static byte[] ParseIP(string ip)
        {
            if (!ip.ContainsExactly('.', 3)) return null;
            string[] vals = ip.Split('.');
            byte[] parts = new byte[4];
            for(int i = 0; i<4; ++i)
                if (!byte.TryParse(vals[i], out parts[i])) return null;
            return parts;
        }
    }
}
