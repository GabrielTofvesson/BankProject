using Client.ConsoleForms;
using ConsoleForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;
using Client.ConsoleForms.Graphics;
using Tofvesson.Crypto;
using Client.Properties;

namespace Client
{
    public class NetContext : Context
    {
        private static readonly RandomProvider provider = new RegularRandomProvider();
        public NetContext(ContextManager manager) : base(manager, "Networking", "Common")
        {
            // Just close when anything is selected and "submitted"
            RegisterSelectListeners((s, i, v) => controller.CloseView(s), "EmptyFieldError", "IPError", "PortError", "ConnectionError");

            bool connecting = false;

            GetView<InputView>("NetConnect").SubmissionsListener = i =>
            {
                if (connecting)
                {
                    controller.Popup("Already connecting!", 1000, ConsoleColor.DarkRed);
                    return;
                }
                bool
                    ip = ParseIP(i.Inputs[0].Text) != null,
                    port = short.TryParse(i.Inputs[1].Text, out short prt) && prt > 0;

                if (ip && port)
                {
                    connecting = true;
                    // Connect to server here
                    BankNetInteractor ita = new BankNetInteractor(i.Inputs[0].Text, prt);
                    /*
                    try
                    {
                        //var t = ita.Connect();
                        //while (!t.IsCompleted)
                        //    if (t.IsCanceled || t.IsFaulted)
                        //    {
                        //        Show("ConnectError");
                        //        return;
                        //    }
                        //    else System.Threading.Thread.Sleep(125);
                    }
                    catch
                    {
                        Show("ConnectionError");
                        connecting = false;
                        return;
                    }
                    */
                    
                    Promise verify = Promise.AwaitPromise(ita.CheckIdentity(new RSA(Resources.e_0x100, Resources.n_0x100), provider.NextUShort()));
                    verify.Subscribe =
                        p =>
                        {
                            void load() => manager.LoadContext(new WelcomeContext(manager, ita));

                            // Add condition check for remote peer verification
                            if (bool.Parse(p.Value)) controller.Popup("Server identity verified!", 1000, ConsoleColor.Green, load);
                            else controller.Popup("Remote server identity could not be verified!", 5000, ConsoleColor.Red, load);
                        };
                    DialogView identityNotify = GetView<DialogView>("IdentityVerify");
                    identityNotify.RegisterSelectListener(
                        (vw, ix, nm) => {
                            verify.Subscribe = null; // Clear subscription
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                            ita.CancelAll();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                            connecting = false;
                        });
                    Show(identityNotify);
                }
                else if (i.Inputs[0].Text.Length == 0 || i.Inputs[1].Text.Length == 0) controller.AddView(views.GetNamed("EmptyFieldError"));
                else if (!ip) Show("IPError");
                else Show("PortError");
            };
        }

        public override void OnCreate() => Show("NetConnect");
        public override void OnDestroy() => HideAll();


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
