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
            entry = ToEncoded(entry);
            for (int i = 0; i < loadedUsers.Count; ++i)
                if (entry.Equals(loadedUsers[i]))
                    loadedUsers[i] = entry;

            for (int i = toRemove.Count - 1; i >= 0; --i)
                if (toRemove[i].Equals(entry.Name))
                    toRemove.RemoveAt(i);

            for (int i = 0; i < changeList.Count; ++i)
                if (changeList[i].Equals(entry.Name))
                    return;

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
            if(!(optional || changeList.Count > 30 || toRemove.Count > 30)) return; // No need to flush
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
                                    User u = User.Parse(ReadEntry(reader), this);
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
            writer.WriteStartElement("User");
            if (u.IsAdministrator) writer.WriteAttributeString("admin", "", "true");
            writer.WriteElementString("Name", u.Name);
            writer.WriteElementString("Balance", u.Balance.ToString());
            writer.WriteElementString("Password", u.PasswordHash);
            writer.WriteElementString("Salt", u.Salt);
            foreach (var tx in u.History)
            {
                writer.WriteStartElement("Transaction");
                writer.WriteElementString(tx.to.Equals(u.Name) ? "From" : "To", tx.to.Equals(u.Name) ? tx.from : tx.to);
                writer.WriteElementString("Balance", tx.amount.ToString());
                if (tx.meta != null && tx.meta.Length != 0) writer.WriteElementString("Meta", tx.meta);
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

        public User GetUser(string name) => FirstUser(u => u.Name.Equals(name));
        public User FirstUser(Predicate<User> p)
        {
            User u;
            foreach (var entry in loadedUsers)
                if (p(u=FromEncoded(entry)))
                    return u;

            foreach (var entry in changeList)
                if (p(u=FromEncoded(entry)))
                {
                    if (!loadedUsers.Contains(entry)) loadedUsers.Add(u);
                    return u;
                }

            using (var reader = XmlReader.Create(DatabaseName))
            {
                if (!Traverse(reader, MasterEntry)) return null;
                while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
                {
                    if (reader.Name.Equals("User"))
                    {
                        User n = User.Parse(ReadEntry(reader), this);
                        if (n != null && p(n=FromEncoded(n)))
                        {
                            if (!loadedUsers.Contains(n)) loadedUsers.Add(n);
                            return n;
                        }
                    }
                }
            }
            return null;
        }

        public bool AddTransaction(string sender, string recipient, long amount, string message = null)
        {
            User from = FirstUser(u => u.Name.Equals(sender));
            User to = FirstUser(u => u.Name.Equals(recipient));

            if (to == null || (from == null && !to.IsAdministrator)) return false;

            Transaction tx = new Transaction(from == null ? "System" : from.Name, to.Name, amount, message);
            to.History.Add(tx);
            AddUser(to);
            if (from != null)
            {
                from.History.Add(tx);
                AddUser(from);
            }
            return true;
        }

        public User[] Users(Predicate<User> p)
        {
            List<User> l = new List<User>();
            User u;
            foreach (var entry in changeList)
                if (p(u=FromEncoded(entry)))
                    l.Add(entry);

            using (var reader = XmlReader.Create(DatabaseName))
            {
                if (!Traverse(reader, MasterEntry)) return null;

                while (SkipSpaces(reader) && reader.NodeType != XmlNodeType.EndElement)
                {
                    if (reader.NodeType == XmlNodeType.EndElement) break;
                    User e = User.Parse(ReadEntry(reader), this);
                    if (e!=null && p(e=FromEncoded(e))) l.Add(e);
                }
            }
            return l.ToArray();
        }

        public bool ContainsUser(string user) => FirstUser(u => u.Name.Equals(user)) != null;
        public bool ContainsUser(User user) => FirstUser(u => u.Name.Equals(user.Name)) != null;

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
            for (int i = 0; i < u.History.Count; ++i)
            {
                u.History[i].to = Encode(u.History[i].to);
                u.History[i].from = Encode(u.History[i].from);
                u.History[i].meta = Encode(u.History[i].meta);
            }
            return u;
        }

        private static User FromEncoded(User entry)
        {
            User u = new User(entry);
            u.Name = Decode(u.Name);
            for (int i = 0; i < u.History.Count; ++i)
            {
                u.History[i].to = Decode(u.History[i].to);
                u.History[i].from = Decode(u.History[i].from);
                u.History[i].meta = Decode(u.History[i].meta);
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
        }

        public class User
        {
            public bool ProblematicTransactions { get; internal set; }
            public string Name { get; internal set; }
            public long Balance { get; set; }
            public bool IsAdministrator { get; set; }
            public string PasswordHash { get; internal set; }
            public string Salt { get; internal set; }
            public List<Transaction> History { get; }
            private User()
            {
                Name = "";
                History = new List<Transaction>();
            }

            public User(User copy) : this()
            {
                this.ProblematicTransactions = copy.ProblematicTransactions;
                this.Name = copy.Name;
                this.Balance = copy.Balance;
                this.IsAdministrator = copy.IsAdministrator;
                this.PasswordHash = copy.PasswordHash;
                this.Salt = copy.Salt;
                this.History.AddRange(copy.History);
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
                PasswordHash = generatePass ? Convert.ToBase64String(KDF.PBKDF2(KDF.HMAC_SHA1, Encoding.UTF8.GetBytes(passHash), Encoding.UTF8.GetBytes(Salt), 8192, 320)) :  passHash;
            }

            public bool Authenticate(string password)
                => Convert.ToBase64String(KDF.PBKDF2(KDF.HMAC_SHA1, Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(Salt), 8192, 320)).Equals(PasswordHash);

            public User AddTransaction(Transaction tx)
            {
                History.Add(tx);
                return this;
            }

            private Entry Serialize()
            {
                Entry root = new Entry("User")
                    .AddNested(new Entry("Name", Name))
                    .AddNested(new Entry("Balance", Balance.ToString()).AddAttribute("omit", "true"));
                foreach (var transaction in History)
                {
                    Entry tx =
                        new Entry("Transaction")
                            .AddAttribute("omit", "true")
                            .AddNested(new Entry(transaction.to.Equals(Name) ? "From" : "To", transaction.to.Equals(Name) ? transaction.from : transaction.to))
                            .AddNested(new Entry("Balance", transaction.amount.ToString()));
                    if (transaction.meta != null) tx.AddNested(new Entry("Meta", transaction.meta));
                    root.AddNested(tx);
                }
                return root;
            }

            internal static User Parse(Entry e, Database db)
            {
                if (!e.Name.Equals("User")) return null;
                User user = new User();
                foreach (var entry in e.NestedEntries)
                {
                    if (entry.Name.Equals("Name")) user.Name = entry.Text;
                    else if (entry.Name.Equals("Balance")) user.Balance = long.TryParse(entry.Text, out long l) ? l : 0;
                    else if (entry.Name.Equals("Transaction"))
                    {
                        string from = null;
                        string to = null;
                        long amount = -1;
                        string meta = "";
                        foreach (var e1 in entry.NestedEntries)
                        {
                            if (e1.Name.Equals("To")) to = e1.Text;
                            else if (e1.Name.Equals("From")) from = e1.Text;
                            else if (e1.Name.Equals("Balance")) amount = long.TryParse(e1.Text, out amount) ? amount : 0;
                            else if (e1.Name.Equals("Meta")) meta = e1.Text;
                        }
                        if ((from == null && to == null) || (from != null && to != null) || amount <= 0) user.ProblematicTransactions = true;
                        else user.History.Add(new Transaction(from, to, amount, meta));
                    }
                    else if (entry.Name.Equals("Password")) user.PasswordHash = entry.Text;
                    else if (entry.Name.Equals("Salt")) user.Salt = entry.Text;
                }
                if (user.Name == null || user.Name.Length == 0 || user.PasswordHash == null || user.Salt == null || user.PasswordHash.Length==0 || user.Salt.Length==0) return null;
                if (user.Balance < 0) user.Balance = 0;

                // Populate transaction names
                foreach (var transaction in user.History)
                    if (transaction.from == null) transaction.from = user.Name;
                    else if (transaction.to == null) transaction.to = user.Name;

                return user;
            }

            public Transaction CreateTransaction(User recipient, long amount, string message = null) => new Transaction(this.Name, recipient.Name, amount, message);

            public override bool Equals(object obj) => obj is User && ((User)obj).Name.Equals(Name);

            public override int GetHashCode()
            {
                return 539060726 + EqualityComparer<string>.Default.GetHashCode(Name);
            }
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

            public User GetTxToUser(Database db) => db.FirstUser(u => u.Name.Equals(to));
            public User GetTxFromUser(Database db) => db.FirstUser(u => u.Name.Equals(from));
        }
    }
}
