using System;
using System.Collections.Generic;
using Tofvesson.Crypto;

namespace Server
{
    public sealed class SessionManager
    {
        private static readonly RandomProvider random = new RegularRandomProvider();
        private readonly List<Session> sessions = new List<Session>();
        private readonly long timeout;
        private readonly int sidLength;

        public List<Session> Sessions { get => sessions; }

        public SessionManager(long timeout, int sidLength = 10)
        {
            this.timeout = timeout;
            this.sidLength = sidLength < 10 ? 10 : sidLength;
        }

        public bool HasSession(string sess, bool byUserName = false)
        {
            foreach (var session in Sessions)
                if ((byUserName && session.user.Name.Equals(sess)) || (!byUserName && session.sessionID.Equals(sess)))
                    return true;
            return false;
        }

        public string GetSession(Database.User user, string invalidSID)
        {
            Update();
            for (int i = 0; i < sessions.Count; ++i)
                if (sessions[i].user.Equals(user))
                    return sessions[i].sessionID;

            Session s = new Session
            {
                sessionID = GenerateRandomSID(invalidSID),
                user = user,
                expiry = DateTime.Now.Ticks + timeout
            };
            sessions.Add(s);
            return s.sessionID;
        }

        public Database.User GetUser(string SID)
        {
            foreach (var session in sessions)
                if (session.sessionID.Equals(SID))
                    return session.user;
            return null;
        }

        public bool Refresh(Database.User user)
        {
            Update();
            for (int i = sessions.Count - 1; i >= 0; --i)
                if (sessions[i].user.Equals(user))
                {
                    Session s = sessions[i];
                    s.expiry = DateTime.Now.Ticks + timeout;
                    sessions[i] = s;
                    return true;
                }
            return false;
        }

        public void Expire(Database.User user)
        {
            Update();
            for (int i = sessions.Count - 1; i >= 0; --i)
                if (sessions[i].user.Equals(user))
                {
                    sessions.RemoveAt(i);
                    return;
                }
            return;
        }

        public bool Refresh(string sid, bool asUser = false)
        {
            Update();
            for (int i = sessions.Count - 1; i >= 0; --i)
                if ((asUser && sessions[i].user.Name.Equals(sid)) || (!asUser && sessions[i].sessionID.Equals(sid)))
                {
                    Session s = sessions[i];
                    s.expiry = DateTime.Now.Ticks + timeout;
                    sessions[i] = s;
                    return true;
                }
            return false;
        }

        public bool Expire(string sid)
        {
            Update();
            for (int i = sessions.Count - 1; i >= 0; --i)
                if (sessions[i].sessionID.Equals(sid))
                {
                    sessions.RemoveAt(i);
                    return true;
                }
            return false;
        }

        public bool CheckSession(string sid, Database.User user)
        {
            foreach (var session in sessions)
                if (session.sessionID.Equals(sid) && session.user.Equals(user))
                    return true;

            return false;
        }

        public void Update()
        {
            for(int i = sessions.Count - 1; i>=0; --i)
                if (sessions[i].expiry < DateTime.Now.Ticks)
                    sessions.RemoveAt(i);
        }

        private string GenerateRandomSID(string invalid)
        {
            string res;
            do res = random.NextString(sidLength);
            while (res.StartsWith(invalid));
            return res;
        }
    }

    public struct Session
    {
        public string sessionID;
        public Database.User user;
        public long expiry; // Measured in ticks
    }
}
