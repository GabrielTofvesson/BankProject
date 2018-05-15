using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Tofvesson.Crypto;
using Tofvesson.Collections;

namespace Server
{
    public sealed class Database
    {
        private static readonly RandomProvider random = new RegularRandomProvider();
        public string[] MasterEntry { get; }
        public string DatabaseName { get; }
        public bool LoadFull { get; set; }

        // Cached changes
        private readonly List<User> changeList = new List<User>();
        private readonly List<User> toRemove = new List<User>();
        private readonly EvictionList<User> loadedUsers = new EvictionList<User>(40);


        public Database(string dbName, string master, bool loadFullDB = false)
        {
            dbName += ".xml";
            MasterEntry = master.Split('/');
            if (!File.Exists(dbName))
            {
                FileStream strm = File.Create(dbName);
                byte[] b;
                strm.Write(b = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n"), 0, b.Length);

                // Generate root element for users
                for (int i = 0; i < MasterEntry.Length; ++i) strm.Write(b = Encoding.UTF8.GetBytes($"<{MasterEntry[i]}>"), 0, b.Length);
                for (int i = MasterEntry.Length - 1; i >= 0; --i) strm.Write(b = Encoding.UTF8.GetBytes($"</{MasterEntry[i]}>"), 0, b.Length);
                strm.Close();
            }
            DatabaseName = dbName;
            LoadFull = loadFullDB;
        }

        // Flush before deletion
        ~Database() { Flush(false); }

        // UpdateUser is just another name for AddUser
        public void UpdateUser(User entry) => AddUser(entry, true);
        public void AddUser(User entry) => AddUser(entry, true);
        private void AddUser(User entry, bool withFlush)
        {
            for (int i = 0; i < loadedUsers.Count; ++i)
                if (entry.Name.Equals(loadedUsers[i].Name))
                    loadedUsers[i] = entry;

            for (int i = toRemove.Count - 1; i >= 0; --i)
                if (toRemove[i].Name.Equals(entry.Name))
                    toRemove.RemoveAt(i);

            for (int i = 0; i < changeList.Count; ++i)
                if (changeList[i].Name.Equals(entry.Name))
                {
                    changeList[i] = entry;
                    return;
                }

            changeList.Add(entry);

            if(withFlush) Flush(true);
        }

        public void RemoveUser(User entry) => RemoveUser(entry, true);
        private void RemoveUser(User entry, bool withFlush)
        {
            entry = ToEncoded(entry);
            for (int i = 0; i < loadedUsers.Count; ++i)
                if (entry.Equals(loadedUsers[i]))
                    loadedUsers.RemoveAt(i);

            for (int i = changeList.Count - 1; i >= 0; --i)
                if (changeList[i].Equals(entry.Name))
                    changeList.RemoveAt(i);

            for (int i = toRemove.Count - 1; i >= 0; --i)
                if (toRemove[i].Equals(entry.Name))
                    return;

            toRemove.Add(entry);

            if(withFlush) Flush(true);
        }

        // Triggers a forceful flush
        public void Flush() => Flush(false);

        // Permissive (cache-dependent) flush
        private void Flush(bool optional)
        {
            if(optional && (changeList.Count < 30 && toRemove.Count < 30)) return; // No need to flush
            string temp = GenerateTempFileName("tmp_", ".xml");
            using(var writer = XmlWriter.Create(temp))
            {
                using(var reader = XmlReader.Create(DatabaseName))
                {
                    int masterDepth = 0;
                    bool trigger = false, wn = false, recent = false;
                    while (wn || reader.Read())
                    {
                        wn = false;
                        if (trigger)
                        {
                            foreach (var user in changeList)
                                WriteUser(writer, user);

                            bool wroteNode = false;
                            while ((wroteNode || reader.Name.Equals("User") || reader.Read()) && reader.NodeType != XmlNodeType.EndElement)
                            {
                                wroteNode = false;
                                if (reader.Name.Equals("User"))
                                {
                                    User u = FromEncoded(User.Parse(ReadEntry(reader), this));
                                    if (u != null)
                                    {
                                        bool shouldWrite = true;
                                        foreach (var toChange in changeList)
                                            if (toChange.Name.Equals(u.Name))
                                            {
                                                shouldWrite = false;
                                                break;
                                            }
                                        if (shouldWrite)
                                            foreach (var remove in toRemove)
                                                if (remove.Name.Equals(u.Name))
                                                {
                                                    shouldWrite = false;
                                                    break;
                                                }
                                        if (shouldWrite) WriteUser(writer, u);
                                    }
                                }
                                else
                                {
                                    wroteNode = true;
                                    writer.WriteNode(reader, true);
                                }
                            }
                            trigger = false;
                            recent = true;
                            writer.WriteEndElement();
                            toRemove.Clear();
                            changeList.Clear();
                        }
                        if (masterDepth != MasterEntry.Length && reader.Name.Equals(MasterEntry[masterDepth]))
                        {
                            trigger = reader.NodeType == XmlNodeType.Element && ++masterDepth == MasterEntry.Length;
                            reader.MoveToContent();
                            writer.WriteStartElement(MasterEntry[masterDepth - 1]);
                        }
                        else if (masterDepth == MasterEntry.Length && recent)
                        {
                            if(masterDepth!=1) writer.WriteEndElement();
                            recent = false;
                        }
                        else
                        {
                            wn = true;
                            writer.WriteNode(reader, true);
                        }
                    }
                }
                writer.Flush();
            }

            File.Delete(DatabaseName);
            File.Move(temp, DatabaseName);
        }

        private static void WriteUser(XmlWriter writer, User u)
        {
            u = ToEncoded(u);
            writer.WriteStartElement("User");
            if (u.IsAdministrator) writer.WriteAttributeString("admin", "", "true");
            writer.WriteElementString("Name", u.Name);
            //writer.WriteElementString("Balance", u.Balance.ToString());
            writer.WriteElementString("Password", u.PasswordHash);
            writer.WriteElementString("Salt", u.Salt);
            foreach(var acc in u.accounts)
            {
                writer.WriteStartElement("Account");
                writer.WriteElementString("Name", acc.name);
                writer.WriteElementString("Balance", acc.balance.ToString());
                foreach (var tx in acc.History)
                {
                    writer.WriteStartElement("Transaction");
                    writer.WriteElementString("FromAccount", tx.fromAccount);
                    writer.WriteElementString("ToAccount", tx.toAccount);
                    writer.WriteElementString(tx.to.Equals(u.Name) ? "From" : "To", tx.to.Equals(u.Name) ? tx.from : tx.to);
                    writer.WriteElementString("Balance", tx.amount.ToString());
                    if (tx.meta != null && tx.meta.Length != 0) writer.WriteElementString("Meta", tx.meta);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private static string GenerateTempFileName(string prefix, string suffix)
        {
            string s;
            do s = prefix + random.NextString((Math.Abs(random.NextInt())%16)+8) + suffix;
            while (File.Exists(s));
            return s;
        }

        private Entry ReadEntry(XmlReader reader)
        {
            Entry e = new Entry(reader.Name);
            if (reader.HasAttributes)
            {
                reader.MoveToAttribute(0);
                do e.Attributes.Add(reader.Name, reader.Value);
                while (reader.MoveToNextAttribute());
            }
            reader.MoveToContent();
            while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element)
                    using (var subRead = reader.ReadSubtree())
                    {
                        SkipSpaces(subRead);
                        e.NestedEntries.Add(ReadEntry(subRead));
                    }
                else if (reader.NodeType == XmlNodeType.Text) e.Text = reader.Value;
            }
            reader.Read();
            return e;
        }

        public User GetUser(string name) => name.Equals("System") ? null : FirstUser(u => u.Name.Equals(name));
        public User FirstUser(Predicate<User> p)
        {
            if (p == null) return null; // Done to conveniently handle system insertions
            foreach (var entry in loadedUsers)
                if (p(entry))
                    return entry;

            foreach (var entry in changeList)
                if (p(entry))
                {
                    if (!loadedUsers.Contains(entry)) loadedUsers.Add(entry);
                    return entry;
                }

            using (var reader = XmlReader.Create(DatabaseName))
            {
                if (!Traverse(reader, MasterEntry)) return null;
                while ((reader.Name.Equals("User") && reader.NodeType != XmlNodeType.EndElement) || (reader.Read() && reader.NodeType != XmlNodeType.EndElement))
                {
                    if (reader.Name.Equals("User"))
                    {
                        User n = FromEncoded(User.Parse(ReadEntry(reader), this));
                        if (n != null && p(n))
                        {
                            if (!loadedUsers.Contains(n)) loadedUsers.Add(n);
                            return n;
                        }
                    }
                }
            }
            return null;
        }

        public bool AddTransaction(string sender, string recipient, decimal amount, string fromAccount, string toAccount, string message = null)
        {
            User from = FirstUser(u => u.Name.Equals(sender));
            User to = FirstUser(u => u.Name.Equals(recipient));
            Account fromAcc = from?.GetAccount(fromAccount);
            Account toAcc = to.GetAccount(toAccount);

            // Errant states
            if (
                to == null ||
                (from == null && !to.IsAdministrator) ||
                toAcc == null ||
                (from != null && fromAcc == null) ||
                (from != null && fromAcc.balance<amount)
                ) return false;

            Transaction tx = new Transaction(from == null ? "System" : from.Name, to.Name, amount, message, fromAccount, toAccount);
            toAcc.History.Add(tx);
            toAcc.balance += amount;
            AddUser(to, false); // Let's not flush unnecessarily
            //UpdateUser(to); // For debugging: Force a flush
            if (from != null)
            {
                fromAcc.History.Add(tx);
                fromAcc.balance -= amount;
                AddUser(from, false);
            }
            return true;
        }

        public User[] Users(Predicate<User> p)
        {
            List<User> l = new List<User>();
            foreach (var entry in changeList)
                if (p(entry))
                    l.Add(entry);

            foreach(var entry in loadedUsers)
                if (!l.Contains(entry) && p(entry))
                    l.Add(entry);

            /*
            using (var reader = XmlReader.Create(DatabaseName))
            {
                if (!Traverse(reader, MasterEntry)) return null;
                
                while (((reader.NodeType==XmlNodeType.Element && reader.Name.Equals("User")) || SkipSpaces(reader)) && reader.NodeType != XmlNodeType.EndElement)
                {
                    if (reader.NodeType == XmlNodeType.EndElement) break;
                    User e = User.Parse(ReadEntry(reader), this);
                    if (e!=null && !l.Contains(e = FromEncoded(e)) && p(e)) l.Add(e);
                }
            }
            */

            using (var reader = XmlReader.Create(DatabaseName))
            {
                if (!Traverse(reader, MasterEntry)) return null;
                while ((reader.Name.Equals("User") && reader.NodeType != XmlNodeType.EndElement) || (reader.Read() && reader.NodeType != XmlNodeType.EndElement))
                {
                    if (reader.Name.Equals("User"))
                    {
                        User n = FromEncoded(User.Parse(ReadEntry(reader), this));
                        if (n != null && p(n))
                        {
                            if (!l.Contains(n)) l.Add(n);
                        }
                    }
                }
            }

            return l.ToArray();
        }

        public bool ContainsUser(string user) => user.Equals("System") || FirstUser(u => u.Name.Equals(user)) != null;
        public bool ContainsUser(User user) => user.Name.Equals("System") || FirstUser(u => u.Name.Equals(user.Name)) != null;

        private bool Traverse(XmlReader reader, params string[] downTo)
        {
            for(int i = 0; i<downTo.Length; ++i)
            {
                while (reader.Read() && !downTo[i].Equals(reader.Name)) ;
                if (!downTo[i].Equals(reader.Name)) return false;
                reader.MoveToContent();
            }
            return true;
        }

        private bool SkipSpaces(XmlReader reader)
        {
            bool b;
            while ((b = reader.Read()) && reader.NodeType == XmlNodeType.Whitespace) ;
            return b;
        }

        private static User ToEncoded(User entry)
        {
            User u = new User(entry);
            u.Name = Encode(u.Name);
            foreach(var account in u.accounts)
            {
                account.name = Encode(account.name);
                foreach(var transaction in account.History)
                {
                    transaction.to = Encode(transaction.to);
                    transaction.from = Encode(transaction.from);
                    if(transaction.meta != null) transaction.meta = Encode(transaction.meta);
                    transaction.fromAccount = Encode(transaction.fromAccount);
                    transaction.toAccount = Encode(transaction.toAccount);
                }
            }
            return u;
        }

        private static User FromEncoded(User entry)
        {
            if (entry == null) return null;
            User u = new User(entry);
            u.Name = Decode(u.Name);
            foreach (var account in u.accounts)
            {
                account.name = Decode(account.name);
                foreach (var transaction in account.History)
                {
                    transaction.to = Decode(transaction.to);
                    transaction.from = Decode(transaction.from);
                    if(transaction.meta != null) transaction.meta = Decode(transaction.meta);
                    transaction.fromAccount = Decode(transaction.fromAccount);
                    transaction.toAccount = Decode(transaction.toAccount);
                }
            }
            return u;
        }

        private static string Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        private static string Decode(string s) => Convert.FromBase64String(s).ToUTF8String();

        internal class Entry
        {
            public string Text { get; set; }
            public string Name { get; set; }
            public Dictionary<string, string> Attributes { get; }   // Transient properties in comparison
            public List<Entry> NestedEntries { get; }               // Semi-transient comparison properties

            public Entry(string name, string text = "")
            {
                Name = name;
                Text = text;
                Attributes = new Dictionary<string, string>();
                NestedEntries = new List<Entry>();
            }
            public Entry GetNestedEntry(Predicate<Entry> p)
            {
                foreach (var entry in NestedEntries)
                    if (p(entry))
                        return entry;
                return null;
            }

            public bool BoolAttribute(string attrName, bool def = false, bool ignoreCase = true)
                => Attributes.ContainsKey(attrName) && bool.TryParse(ignoreCase ? Attributes[attrName].ToLower() :Attributes[attrName], out bool b) ? b : def;

            public Entry AddNested(Entry e)
            {
                NestedEntries.Add(e);
                return this;
            }

            public Entry AddNested(string name, string text) => AddNested(new Entry(name, text));

            public Entry AddAttribute(string key, string value)
            {
                Attributes[key] = value;
                return this;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is Entry)) return false;
                Entry cmp = (Entry)obj;
                if (cmp.Attributes.Count != Attributes.Count || cmp.NestedEntries.Count != NestedEntries.Count || !Text.Equals(cmp.Text) || !Name.Equals(cmp.Name)) return false;
                foreach(var entry in NestedEntries)
                {
                    if (entry.BoolAttribute("omit")) goto Next;
                    foreach (var cmpEntry in cmp.NestedEntries)
                        if (cmpEntry.BoolAttribute("omit")) continue;
                        else if (cmpEntry.Equals(entry)) goto Next;
                    return false;
                    Next: { }
                }

                return true;
            }

            public override int GetHashCode()
            {
                var hashCode = 495068346;
                hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Text);
                hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
                hashCode = hashCode * -1521134295 + EqualityComparer<Dictionary<string, string>>.Default.GetHashCode(Attributes);
                hashCode = hashCode * -1521134295 + EqualityComparer<List<Entry>>.Default.GetHashCode(NestedEntries);
                return hashCode;
            }
        }

        public class Account
        {
            public User owner;
            public decimal balance;
            public string name;
            public List<Transaction> History { get; }
            public Account(User owner, decimal balance, string name)
            {
                History = new List<Transaction>();
                this.owner = owner;
                this.balance = balance;
                this.name = name;
            }
            public Account(Account copy) : this(copy.owner, copy.balance, copy.name)
            {
                // Value copy, not reference copy
                foreach (var tx in copy.History)
                    History.Add(new Transaction(tx.from, tx.to, tx.amount, tx.meta, tx.fromAccount, tx.toAccount));
            }
            public Account AddTransaction(Transaction tx)
            {
                History.Add(tx);
                return this;
            }

            public override string ToString()
            {
                StringBuilder builder = new StringBuilder(balance.ToString());
                foreach (var tx in History)
                {
                    builder
                    .Append('{')
                    .Append(tx.from.ToBase64String())
                    .Append('&')
                    .Append(tx.fromAccount.ToBase64String())
                    .Append('&')
                    .Append(tx.to.ToBase64String())
                    .Append('&')
                    .Append(tx.toAccount.ToBase64String())
                    .Append('&')
                    .Append(tx.amount.ToString());
                    if (tx.meta != null) builder.Append('&').Append(tx.meta.ToBase64String());
                    //builder.Append('}');
                }
                return builder.ToString();
            }
        }

        public class User
        {
            public bool ProblematicTransactions { get; internal set; }
            public string Name { get; internal set; }
            public bool IsAdministrator { get; set; }
            public string PasswordHash { get; internal set; }
            public string Salt { get; internal set; }
            public List<Account> accounts = new List<Account>();

            private User()
            { }

            public User(User copy)
            {
                this.ProblematicTransactions = copy.ProblematicTransactions;
                this.Name = copy.Name;
                this.IsAdministrator = copy.IsAdministrator;
                this.PasswordHash = copy.PasswordHash;
                this.Salt = copy.Salt;
                foreach (var acc in copy.accounts) accounts.Add(new Account(acc));
            }

            public User(string name, string passHash, string salt, bool generatePass = false, bool admin = false)
                : this(name, passHash, Encoding.UTF8.GetBytes(salt), generatePass, admin)
            { }

            public User(string name, string passHash, byte[] salt, bool generatePass = false, bool admin = false)
            {
                Name = name;
                IsAdministrator = admin;
                Salt = Convert.ToBase64String(salt);
                PasswordHash = generatePass ? ComputePass(passHash) :  passHash;
            }

            public string ComputePass(string pass)
                => Convert.ToBase64String(KDF.PBKDF2(KDF.HMAC_SHA1, Encoding.UTF8.GetBytes(pass), Encoding.UTF8.GetBytes(Salt), 8192, 320));

            public bool Authenticate(string password)
                => Convert.ToBase64String(KDF.PBKDF2(KDF.HMAC_SHA1, Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(Salt), 8192, 320)).Equals(PasswordHash);

            public void AddAccount(Account a) => accounts.Add(a);
            public Account GetAccount(string name) => accounts.FirstOrDefault(a => a.name.Equals(name));
            private Entry Serialize()
            {
                Entry root = new Entry("User")
                    .AddNested(new Entry("Name", Name));
                foreach (var account in accounts)
                {
                    Entry acc = new Entry("Account")
                        .AddNested("Name", account.name)
                        .AddNested(new Entry("Balance", account.balance.ToString()).AddAttribute("omit", "true"));
                    foreach (var transaction in account.History)
                    {
                        Entry tx =
                            new Entry("Transaction")
                                .AddAttribute("omit", "true")
                                .AddNested(transaction.to.Equals(Name) ? "From" : "To", transaction.to.Equals(Name) ? transaction.from : transaction.to)
                                .AddNested("FromAccount", transaction.fromAccount)
                                .AddNested("ToAccount", transaction.toAccount)
                                .AddNested("Balance", transaction.amount.ToString());
                        if (transaction.meta != null) tx.AddNested("Meta", transaction.meta);
                        acc.AddNested(tx);
                    }
                    root.AddNested(acc);
                }
                return root;
            }

            internal static User Parse(Entry e, Database db)
            {
                if (!e.Name.Equals("User")) return null;
                User user = new User();
                foreach (var attribute in e.Attributes)
                    if (attribute.Key.Equals("admin") && attribute.Value.Equals("true"))
                        user.IsAdministrator = true;
                foreach (var entry in e.NestedEntries)
                {
                    if (entry.Name.Equals("Name")) user.Name = entry.Text;
                    else if (entry.Name.Equals("Account"))
                    {
                        string name = null;
                        decimal balance = 0;
                        List<Transaction> history = new List<Transaction>();
                        foreach (var accountData in entry.NestedEntries)
                        {
                            if (accountData.Name.Equals("Name")) name = accountData.Text;
                            else if (accountData.Name.Equals("Transaction"))
                            {
                                string fromAccount = null;
                                string toAccount = null;
                                string from = null;
                                string to = null;
                                decimal amount = -1;
                                string meta = "";
                                foreach (var e1 in accountData.NestedEntries)
                                {
                                    if (e1.Name.Equals("To")) to = e1.Text;
                                    else if (e1.Name.Equals("From")) from = e1.Text;
                                    else if (e1.Name.Equals("FromAccount")) fromAccount = e1.Text;
                                    else if (e1.Name.Equals("ToAccount")) toAccount = e1.Text;
                                    else if (e1.Name.Equals("Balance")) amount = decimal.TryParse(e1.Text, out amount) ? amount : 0;
                                    else if (e1.Name.Equals("Meta")) meta = e1.Text;
                                }
                                if ( // Errant states for transaction data
                                    (from == null && to == null) ||
                                    (from != null && to != null) ||
                                    amount <= 0 ||
                                    fromAccount == null ||
                                    toAccount == null
                                    )
                                    user.ProblematicTransactions = true;
                                else history.Add(new Transaction(from, to, amount, meta, fromAccount, toAccount));
                            }
                            else if (accountData.Name.Equals("Balance")) balance = decimal.TryParse(accountData.Text, out decimal l) ? l : 0;
                        }
                        if (name == null || balance < 0)
                        {
                            Output.Fatal($"Found errant account entry! Detected user name: {user.Name}");
                            return null; // This is a hard error
                        }
                        Account a = new Account(user, balance, name);
                        a.History.AddRange(history);
                        user.AddAccount(a);
                    }
                    else if (entry.Name.Equals("Password")) user.PasswordHash = entry.Text;
                    else if (entry.Name.Equals("Salt")) user.Salt = entry.Text;
                }
                if (user.Name == null || user.Name.Length == 0 || user.PasswordHash == null || user.Salt == null || user.PasswordHash.Length==0 || user.Salt.Length==0) return null;

                // Populate transaction names
                foreach (var account in user.accounts)
                    foreach (var transaction in account.History)
                        if (transaction.from == null) transaction.from = user.Name;
                        else if (transaction.to == null) transaction.to = user.Name;

                return user;
            }

            public Transaction CreateTransaction(User recipient, long amount, Account fromAccount, Account toAccount, string message = null) =>
                new Transaction(this.Name, recipient.Name, amount, message, fromAccount.name, toAccount.name);

            public override bool Equals(object obj) => obj is User && ((User)obj).Name.Equals(Name);

            public override int GetHashCode()
            {
                return 539060726 + EqualityComparer<string>.Default.GetHashCode(Name);
            }
        }

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

            public User GetTxToUser(Database db) => db.FirstUser(u => u.Name.Equals(to));
            public User GetTxFromUser(Database db) => db.FirstUser(u => u.Name.Equals(from));
        }
    }
}
