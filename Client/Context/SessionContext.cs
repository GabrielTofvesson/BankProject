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
        private readonly FixedQueue<Tuple<string, decimal>> accountDataCache = new FixedQueue<Tuple<string, decimal>>(64);
        private bool accountChange = false;


        public SessionContext(ContextManager manager, BankNetInteractor interactor) : base(manager, "Session", "Common")
        {
            this.interactor = interactor;
            scheduleDestroy = !interactor.IsLoggedIn;

            RegisterAutoHide("account_create", "account_info", "password_update", "exit_prompt", "account_show");

            GetView<DialogView>("Success").RegisterSelectListener((v, i, s) => HandleLogout());

            // Menu option setup
            ListView options = GetView<ListView>("menu_options");
            options.GetView<ButtonView>("exit").SetEvent(v => HandleLogout());

            options.GetView<ButtonView>("view").SetEvent(v =>
            {
                if (accountChange) RefreshAccountList();
                if (!accountsGetter.HasValue) Show("data_fetch");
                accountsGetter.Subscribe = p =>
                {
                    accountsGetter.Unsubscribe();
                    Hide("data_fetch");

                    void SubmitListener(View listener)
                    {
                        ButtonView view = listener as ButtonView;

                        void ShowAccountData(string name, decimal balance)
                        {
                            // Build dialog view manually
                            var show = new DialogView(
                                new ViewData("DialogView")

                                // Layout parameters
                                .SetAttribute("padding_left", 2)
                                .SetAttribute("padding_right", 2)
                                .SetAttribute("padding_top", 1)
                                .SetAttribute("padding_bottom", 1)

                                // Option buttons
                                .AddNested(new ViewData("Options").AddNestedSimple("Option", GetIntlString("GENERIC_dismiss")))

                                // Message
                                .AddNestedSimple("Text", GetIntlString("SE_info").Replace("$0", name).Replace("$1", balance.ToString())),

                                // No translation (it's already handled)
                                LangManager.NO_LANG);

                            show.RegisterSelectListener((_, s, l) => Hide(show));
                            Show(show);
                        }

                        // TODO: Show account info
                        var account = AccountLookup(view.Text);
                        if (account == null)
                        {
                            // TODO: Get account data from server + cache data
                            Show("data_fetch");
                            Promise info_promise = Promise.AwaitPromise(interactor.AccountInfo(view.Text));
                            info_promise.Subscribe = evt =>
                            {
                                Hide("data_fetch");
                                if (evt.Value.StartsWith("ERROR") || !Account.TryParse(evt.Value, out var act))
                                    controller.Popup(GetIntlString("GENERIC_error"), 3000, ConsoleColor.Red);
                                else
                                {
                                    accountDataCache.Enqueue(new Tuple<string, decimal>(view.Text, act.balance)); // Cache result
                                    ShowAccountData(view.Text, act.balance);
                                }
                                
                            };
                        }
                        else ShowAccountData(account.Item1, account.Item2);
                    }

                    var list = GetView<ListView>("account_show");
                    list.RemoveIf(t => !t.Item1.Equals("close"));
                    var data = p.Value.Split('&');
                    bool b = data.Length == 1 && data[0].Length == 0;
                    Tuple<string, View>[] listData = new Tuple<string, View>[data.Length - (b?1:0)];
                    if(!b)
                        for(int i = 0; i<listData.Length; ++i)
                        {
                            ButtonView t = new ButtonView(new ViewData("ButtonView").AddNestedSimple("Text", data[i].FromBase64String()), LangManager.NO_LANG); // Don't do translations
                            t.SetEvent(SubmitListener);
                            listData[i] = new Tuple<string, View>(t.Text, t);
                        }
                    string dismiss = GetIntlString("GENERIC_dismiss");
                    ButtonView exit = list.GetView<ButtonView>("close");
                    exit.SetEvent(_ => Hide(list));
                    list.AddViews(0, listData); // Insert generated buttons before predefined "close" button
                    Show(list);
                };
            });

            GetView<InputView>("password_update").SubmissionsListener = v =>
            {
                bool hasError = v.Inputs[0].Text.Length == 0;
                if (hasError)
                {
                    // Notify user, as well as mark the errant input field
                    v.Inputs[0].SelectBackgroundColor = ConsoleColor.Red;
                    v.Inputs[0].BackgroundColor = ConsoleColor.DarkRed;
                    controller.Popup(GetIntlString("ERR_empty"), 3000, ConsoleColor.Red);
                }
                if(v.Inputs[1].Text.Length == 0)
                {
                    v.Inputs[1].SelectBackgroundColor = ConsoleColor.Red;
                    v.Inputs[1].BackgroundColor = ConsoleColor.DarkRed;
                    if(!hasError) controller.Popup(GetIntlString("ERR_empty"), 3000, ConsoleColor.Red);
                    return; // No need to continue, we have notified the user. There is no valid information to operate on past this point
                }
                if (!v.Inputs[0].Text.Equals(v.Inputs[1].Text))
                {
                    controller.Popup(GetIntlString("SU_mismatch"), 3000, ConsoleColor.Red);
                    return;
                }
                Show("update_stall");
                Task<Promise> t = interactor.UpdatePassword(v.Inputs[0].Text);
                Promise.AwaitPromise(t).Subscribe = p =>
                {
                    Hide("update_stall");
                    Hide("password_update");
                    v.Inputs[0].ClearText();
                    v.Inputs[1].ClearText();
                    v.SelectedField = 0;
                };
            };

            // Actual "create account" input box thingy
            var input = GetView<InputView>("account_create");
            input.SubmissionsListener = __ =>
            {
                if (input.Inputs[0].Text.Length == 0)
                {
                    input.Inputs[0].SelectBackgroundColor = ConsoleColor.Red;
                    input.Inputs[0].BackgroundColor = ConsoleColor.DarkRed;
                    controller.Popup(GetIntlString("ERR_empty"), 3000, ConsoleColor.Red);
                }
                else
                {
                    void AlreadyExists()
                        => controller.Popup(GetIntlString("SE_account_exists").Replace("$0", input.Inputs[0].Text), 2500, ConsoleColor.Red, () => Hide(input));

                    var act = AccountLookup(input.Inputs[0].Text);
                    if (act != null) AlreadyExists();
                    else
                    {
                        Show("account_stall");
                        Promise accountPromise = Promise.AwaitPromise(interactor.CreateAccount(input.Inputs[0].Text));
                        accountPromise.Subscribe = p =>
                        {
                            if (bool.Parse(p.Value))
                            {
                                controller.Popup(GetIntlString("SE_account_success"), 750, ConsoleColor.Green, () => Hide(input));
                                accountChange = true;
                            }
                            else AlreadyExists();
                            Hide("account_stall");
                        };
                    }
                }
            };
            input.InputListener = (v, c, i) =>
            {
                c.BackgroundColor = v.DefaultBackgroundColor;
                c.SelectBackgroundColor = v.DefaultSelectBackgroundColor;
                return true;
            };

            options.GetView<ButtonView>("add").SetEvent(_ => Show(input));

            // Set up a listener to reset color scheme
            GetView<InputView>("password_update").InputListener = (v, c, i) =>
            {
                c.BackgroundColor = v.DefaultBackgroundColor;
                c.SelectBackgroundColor = v.DefaultSelectBackgroundColor;
                return true;
            };

            // Update password
            options.GetView<ButtonView>("update").SetEvent(v => Show("password_update"));

            options.OnBackEvent = v =>
            {
                Show("exit_prompt");
            };

            GetView<DialogView>("exit_prompt").RegisterSelectListener((v, i, s) =>
            {
                if (i == 0) Hide("exit_prompt");
                else
                {
                    interactor.Logout();
                    controller.ShouldExit = true;
                }
            });

            if (!scheduleDestroy)
            {
                // We have a valid context!
                RefreshUserInfo();      // Get user info
                RefreshAccountList();   // Get account list for user
            }
        }

        private void RefreshAccountList()
        {
            accountsGetter = Promise.AwaitPromise(interactor.ListUserAccounts()); // Get accounts associated with this user

            accountsGetter.Subscribe = p =>
            {
                var data = p.Value.Split('&');
                accounts = new List<string>();
                accounts.AddRange(data);
            };
        }

        private void RefreshUserInfo()
        {
            userDataGetter = Promise.AwaitPromise(interactor.UserInfo()); // Get basic user info

            userDataGetter.Subscribe = p =>
            {
                var data = p.Value.Split('&');
                username = data[0].FromBase64String();
                isAdministrator = bool.Parse(data[1]);
            };
        }

        private void HandleLogout(bool automatic = false)
        {
            interactor.Logout();
            controller.Popup(GetIntlString($"SE_{(automatic ? "auto" : "")}lo"), 2500, ConsoleColor.DarkMagenta, () => manager.LoadContext(new NetContext(manager)));
        }

        private Tuple<string, decimal> AccountLookup(string name)
        {
            foreach (var cacheEntry in accountDataCache)
                if (cacheEntry.Item1.Equals(name))
                    return cacheEntry;
            return null;
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
