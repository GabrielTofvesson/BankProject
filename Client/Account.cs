using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public class Account
    {
        public decimal balance;
        public List<Transaction> History { get; }
        public Account(decimal balance)
        {
            History = new List<Transaction>();
            this.balance = balance;
        }
        public Account(Account copy) : this(copy.balance)
            => History.AddRange(copy.History);
        public Account AddTransaction(Transaction tx)
        {
            History.Add(tx);
            return this;
        }

        public static Account Parse(string s)
        {
            var data = s.Split('{');
            if(!decimal.TryParse(data[0], out var balance))
                throw new ParseException("String did not represent a valid account");
            Account a = new Account(balance);
            for (int i = 1; i < data.Length; ++i)
                a.AddTransaction(Transaction.Parse(data[i]));
            return a;
        }

        public static bool TryParse(string s, out Account account)
        {
            try
            {
                account = Account.Parse(s);
                return true;
            }
            catch
            {
                account = null;
                return false;
            }
        }
    }
}
