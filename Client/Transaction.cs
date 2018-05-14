using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Client
{
    public class Transaction
    {
        public string fromAccount;
        public string toAccount;
        public string from;
        public string to;
        public decimal amount;
        public string meta;

        public Transaction(string from, string to, decimal amount, string meta, string fromAccount, string toAccount)
        {
            this.fromAccount = fromAccount;
            this.toAccount = toAccount;
            this.from = from;
            this.to = to;
            this.amount = amount;
            this.meta = meta;
        }

        public static Transaction Parse(string txData)
        {
            var data = txData.Split('&');
            if (data.Length < 5 || !decimal.TryParse(data[4], out var amount)) throw new ParseException("String did not represent a transaction!");
            return new Transaction(
                data[2].FromBase64String(),
                data[1].FromBase64String(),
                amount,
                data.Length == 6 ? data[5].FromBase64String() : null,
                data[3].FromBase64String(),
                data[1].FromBase64String()
                );
        }
    }
}
