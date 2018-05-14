using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tofvesson.Common
{
    // A custom queue implementation with a fixed size
    // Almost directly copied from https://gist.github.com/GabrielTofvesson/1cfbb659e7b2f7cfb6549c799b0864f3
    public class FixedQueue<T> : IEnumerable<T>
    {
        protected readonly T[] queue;
        protected int queueCount = 0;
        protected int queueStart;

        public int Count { get => queueCount; }

        public FixedQueue(int maxSize)
        {
            queue = new T[maxSize];
            queueStart = 0;
        }

        // Add an item to the queue
        public bool Enqueue(T t)
        {
            queue[(queueStart + queueCount) % queue.Length] = t;
            if (++queueCount > queue.Length)
            {
                --queueCount;
                return true;
            }
            return false;
        }

        // Remove an item from the queue
        public T Dequeue()
        {
            if (--queueCount == -1) throw new IndexOutOfRangeException("Cannot dequeue empty queue!");
            T res = queue[queueStart];
            queue[queueStart] = default(T); // Remove reference to item
            queueStart = (queueStart + 1) % queue.Length;
            return res;
        }

        // Indexing for the queue
        public T ElementAt(int index) => queue[(queueStart + index) % queue.Length];

        // Enumeration
        public virtual IEnumerator<T> GetEnumerator() => new QueueEnumerator<T>(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Enumerator for this queue
        public sealed class QueueEnumerator<T> : IEnumerator<T>
        {
            private int offset = -1;
            private readonly FixedQueue<T> queue;

            internal QueueEnumerator(FixedQueue<T> queue) => this.queue = queue;

            object IEnumerator.Current => this.Current;
            public T Current => offset == -1 ? default(T) : queue.ElementAt(offset); // Get current item or (null) if MoveNext() hasn't been called
            public void Dispose() { }                                                           // NOP
            public bool MoveNext() => offset < queue.Count && ++offset < queue.Count;         // Increment index tracker (offset)
            public void Reset() => offset = -1;
        }
    }
}
