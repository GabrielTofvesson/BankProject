using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Tofvesson.Crypto;

namespace Common
{
    public class User
    {
        public bool ProblematicTransactions { get; private set; }
        public string Name { get; private set; }
        public long Balance { get; set; }
        public bool IsAdministrator { get; set; }
        public string PasswordHash { get; private set; }
        public string Salt { get; private set; }
        public List<Transaction> History { get; }
        private User()
        {
            Name = "";
            History = new List<Transaction>();
        }

        public User(string name, string passHash, string salt, long balance, bool generatePass = false, List<Transaction> transactionHistory = null, bool admin = false)
            : this(name, passHash, Encoding.UTF8.GetBytes(salt), balance, generatePass, transactionHistory, admin)
        { }

        public User(string name, string passHash, byte[] salt, long balance, bool generatePass = false, List<Transaction> transactionHistory = null, bool admin = false)
        {
            History = transactionHistory ?? new List<Transaction>();
            Balance = balance;
            Name = name;
            IsAdministrator = admin;
            Salt = Convert.ToBase64String(salt);
            PasswordHash = generatePass ? Convert.ToBase64String(KDF.PBKDF2(KDF.HMAC_SHA1, Encoding.UTF8.GetBytes(passHash), Encoding.UTF8.GetBytes(Salt), 8192, 320)) : passHash;
        }

        public bool Authenticate(string password)
            => Convert.ToBase64String(KDF.PBKDF2(KDF.HMAC_SHA1, Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(Salt), 8192, 320)).Equals(PasswordHash);

        public User AddTransaction(Transaction tx)
        {
            History.Add(tx);
            return this;
        }

        public Transaction CreateTransaction(User recipient, long amount, string message = null) => new Transaction(this.Name, recipient.Name, amount, message);

        public override bool Equals(object obj) => obj is User && ((User)obj).Name.Equals(Name);

        public override int GetHashCode()
            => 539060726 + EqualityComparer<string>.Default.GetHashCode(Name);

        /*
        public string Serialize()
        {
            
        }

        public static User Deserialize(string ser)
        {

        }
        */
    }

    public class Transaction
    {
        public string from;
        public string to;
        public long amount;
        public string meta;

        public Transaction(string from, string to, long amount, string meta)
        {
            this.from = from;
            this.to = to;
            this.amount = amount;
            this.meta = meta;
        }

        public string Serialize()
        {
            XmlDocument doc = new XmlDocument();
            XmlElement el = doc.CreateElement("Transaction");

            XmlElement to = doc.CreateElement("To");
            to.InnerText = this.to;

            XmlElement from = doc.CreateElement("From");
            from.InnerText = this.from;

            XmlElement amount = doc.CreateElement("Balance");
            amount.InnerText = amount.ToString();

            el.AppendChild(to).AppendChild(from).AppendChild(amount);
            if (meta != null)
            {
                XmlElement msg = doc.CreateElement("Meta");
                msg.InnerText = meta;
                el.AppendChild(msg);
            }

            return el.ToString();
        }

        public static Transaction Deserialize(string ser)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(ser);
            return null;
        }
    }
}
