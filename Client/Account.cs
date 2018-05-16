using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public class Account
    {
        public enum AccountType { Savings, Checking }
        public decimal balance;
        public List<Transaction> History { get; }
        public AccountType type;
        public Account(decimal balance, AccountType type)
        {
            History = new List<Transaction>();
            this.balance = balance;
            this.type = type;
        }
        public Account(Account copy) : this(copy.balance, copy.type)
            => History.AddRange(copy.History);
        public Account AddTransaction(Transaction tx)
        {
            History.Add(tx);
            return this;
        }

        public static Account Parse(string s)
        {
            var data = s.Split('{');
            var attr = data[0].Split('&');
            if(attr.Length!=2 || !decimal.TryParse(attr[0], out var balance) || !int.TryParse(attr[1], out var type))
                throw new ParseException("String did not represent a valid account");
            Account a = new Account(balance, (AccountType)type);
            for (int i = 1; i < data.Length; ++i)
                a.AddTransaction(Transaction.Parse(data[i]));
            return a;
        }

        public static bool TryParse(string s, out Account account)
        {
            try
            {
                account = Parse(s);
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
