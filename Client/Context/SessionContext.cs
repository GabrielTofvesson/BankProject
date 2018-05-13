using Client.ConsoleForms;
using Client.ConsoleForms.Graphics;
using Client.ConsoleForms.Parameters;
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
        private bool scheduleDestroy;
        private Promise userDataGetter;
        private Promise accountsGetter;
        private List<string> accounts = null;
        private string username;
        private bool isAdministrator = false;


        public SessionContext(ContextManager manager, BankNetInteractor interactor) : base(manager, "Session", "Common")
        {
            this.interactor = interactor;
            scheduleDestroy = !interactor.IsLoggedIn;

            GetView<DialogView>("Success").RegisterSelectListener((v, i, s) =>
            {
                interactor.Logout();
                manager.LoadContext(new NetContext(manager));
            });

            // Menu option setup
            ListView options = GetView<ListView>("menu_options");
            options.GetView<ButtonView>("exit").SetEvent(v =>
            {
                interactor.Logout();
                manager.LoadContext(new NetContext(manager));
            });

            options.GetView<ButtonView>("view").SetEvent(v =>
            {
                if (!accountsGetter.HasValue) Show("data_fetch");
                accountsGetter.Subscribe = p =>
                {
                    Hide("data_fetch");

                    void SubmitListener(View listener)
                    {
                        ButtonView view = listener as ButtonView;

                    }

                    var list = GetView<ListView>("account_show");
                    var data = p.Value.Split('&');
                    bool b = data.Length == 1 && data[0].Length == 0;
                    Tuple<string, View>[] listData = new Tuple<string, View>[data.Length - (b?1:0)];
                    if(!b)
                        for(int i = 0; i<listData.Length; ++i)
                        {
                            ButtonView t = new ButtonView(new ViewData("ButtonView").AddNestedSimple("Text", data[i]), LangManager.NO_LANG); // Don't do translations
                            t.SetEvent(SubmitListener);
                            listData[i] = new Tuple<string, View>(data[i].FromBase64String(), t);
                        }
                    string dismiss = GetIntlString("@string/GENERIC_dismiss");
                    ButtonView exit = list.GetView<ButtonView>("close");
                    exit.SetEvent(_ => Hide(list));
                    list.AddViews(listData);
                    Show(list);
                };
            });

            // Update password
            options.GetView<ButtonView>("password_update").SetEvent(v =>
            {

            });

            if (!scheduleDestroy)
            {
                // We have a valid context!
                userDataGetter = Promise.AwaitPromise(interactor.UserInfo()); // Get basic user info
                accountsGetter = Promise.AwaitPromise(interactor.ListUserAccounts()); // Get accounts associated with this user

                userDataGetter.Subscribe = p =>
                {
                    var data = p.Value.Split('&');
                    username = data[0].FromBase64String();
                    isAdministrator = bool.Parse(data[1]);
                };

                accountsGetter.Subscribe = p =>
                {
                    var data = p.Value.Split('&');
                    accounts = new List<string>();
                    accounts.AddRange(data);
                };
            }
        }

        private void HandleLogout()
        {

        }

        public override void OnCreate()
        {
            if (scheduleDestroy) manager.LoadContext(new WelcomeContext(manager, interactor));
            else Show("menu_options");
        }

        public override void OnDestroy()
        {
            base.HideAll();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            interactor.CancelAll();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }
}
