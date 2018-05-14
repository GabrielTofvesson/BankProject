using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public delegate void Event(Promise p);
    public class Promise
    {
        internal Promise handler = null; // For chained promise management
        private Event evt;
        public string Value { get; internal set; }
        public bool HasValue { get; internal set; }
        public Event Subscribe
        {
            get => evt;
            set
            {
                // Allows clearing subscriptions
                if (evt == null || value == null) evt = value;
                else evt += value;
                if (HasValue)
                    evt(this);
            }
        }
        public static Promise AwaitPromise(Task<Promise> p)
        {
            //if (!p.IsCompleted) p.RunSynchronously();
            p.Wait();
            return p.Result;
        }

        public void Unsubscribe() => evt = null;
    }
}
