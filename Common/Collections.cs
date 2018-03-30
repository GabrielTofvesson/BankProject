using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace Tofvesson.Collections
{
    public class BoundedList<T> : IEnumerable<T>
    {
        protected const float GROW_FACTOR = 1.75f;
        protected const float SHRINK_FACTOR = 1.25f;

        protected readonly int maxCapacity;
        protected T[] values;
        public int Count { get; private set; }

        public T this[int i]
        {
            get => ElementAt(i);
            set
            {
                DoRangeCheck(i);
                values[i] = value;
            }
        }

        public BoundedList(int maxCapacity = -1, int initialCapacity = 10)
        {
            this.maxCapacity = maxCapacity < 0 ? -1 : maxCapacity;
            values = new T[maxCapacity == -1 ? Max(initialCapacity, 0) : Min(maxCapacity, Max(initialCapacity, 0))];
        }

        private static int Min(int i1, int i2) => i1 > i2 ? i2 : i1;
        private static int Max(int i1, int i2) => i1 > i2 ? i1 : i2;

        public BoundedList(int maxCapacity, IEnumerable<T> collection) : this(maxCapacity, collection.Count())
        {
            int track = 0;
            IEnumerator<T> enumerator = collection.GetEnumerator();
            while(enumerator.MoveNext() && track < maxCapacity)
            {
                Add(enumerator.Current);
                ++track;
            }
        }

        public virtual bool Add(T t)
        {
            if (Count == maxCapacity) return false;
            if (Count == values.Length) Resize(Count * GROW_FACTOR);
            values[Count] = t;
            ++Count;
            return true;
        }

        public virtual bool Remove(T t) => RemoveIf(t1 => (t == null && t1 == null) || (t != null && t.Equals(t1))) > 0;

        public int RemoveIf(Predicate<T> p)
        {
            int removed = 0;
            for (int c = 0; c < Count; ++c)
                if (p(values[c]))
                {
                    _RemoveAt(c);
                    ++removed;
                }
            if (values.Length >= Count * SHRINK_FACTOR) Resize(Count * SHRINK_FACTOR);
            return removed;
        }

        public virtual void RemoveAt(int i)
        {
            _RemoveAt(i);
            if (values.Length >= Count * SHRINK_FACTOR) Resize(Count * SHRINK_FACTOR);
        }

        public virtual T ElementAt(int i)
        {
            DoRangeCheck(i);
            return values[i];
        }

        public virtual T[] ToArray()
        {
            T[] t = new T[Count];
            Array.Copy(values, t, Count);
            return t;
        }

        protected virtual void _RemoveAt(int i)
        {
            DoRangeCheck(i);
            for (int j = i + 1; j < Count; ++j) values[j - 1] = values[j];
            values[Count - 1] = default(T); // Don't keep references in case GC needs to claim the object
            --Count;
        }

        protected virtual void Resize(float targetSize_f)
        {
            int targetSize = maxCapacity == -1 ? Math.Max((int)Math.Round(targetSize_f), 0) : Math.Max(0, Math.Min((int)Math.Round(targetSize_f), maxCapacity));
            T[] surrogate = new T[targetSize];
            Array.Copy(values, surrogate, Math.Min(targetSize, Count));
            values = surrogate;
        }

        protected void DoRangeCheck(int i)
        {
            if (i < 0 || i >= Count) throw new IndexOutOfRangeException();
        }

        public virtual IEnumerator<T> GetEnumerator() => new BoundedListEnumerator<T>(this);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class BoundedListEnumerator<K> : IEnumerator<K>
        {
            public K Current => list.values[current];
            object IEnumerator.Current => Current;

            private readonly BoundedList<K> list;
            private int current = -1;

            public BoundedListEnumerator(BoundedList<K> list)
            {
                this.list = list;
            }

            public void Dispose() { }

            public bool MoveNext() => ++current < list.Count;

            public void Reset() => current = 0;
        }
    }

    public class EvictionList<T> : BoundedList<T>
    {
        public EvictionList(int maxCapacity = -1, int initialCapacity = 10) : base(maxCapacity, initialCapacity)
        { }

        public override bool Add(T t)
        {
            if (Count == maxCapacity) RemoveAt(0);
            return base.Add(t);
        }
    }

    public static class Collections
    {
        public static M Get<M, K, T>(this Dictionary<K, T> dict, K key)
        {
            object d;
            if (dict.ContainsKey(key) && dict[key] != null && dict[key] is M) d = dict[key];
            else d = default(M);
            return (M)d;
        }
        public static Dictionary<K, T> Replace<K, T>(this Dictionary<K, T> dict, K key, T replace)
        {
            dict[key] = replace;
            return dict;
        }
        public static int CollectiveLength(this Tuple<string, string>[] values, bool first)
        {
            int len = 0;
            foreach (var val in values)
                len += (first ? val.Item1 : val.Item2)?.Length ?? 0;
            return len;
        }
        public static T[] Collect<T>(this Tuple<T, T>[] values, bool first)
        {
            T[] collect = new T[values.Length];
            for (int i = 0; i < values.Length; ++i) collect[i] = (first ? values[i].Item1 : values[i].Item2);
            return collect;
        }
        public static V GetNamed<T, V>(this List<Tuple<T, V>> list, T key)
        {
            foreach (var element in list)
                if (element != null && ObjectEquals(key, element.Item1))
                    return element.Item2;
            return default(V);
        }
        public static V GetNamed<T, V>(this ReadOnlyCollection<Tuple<T, V>> list, T key)
        {
            foreach (var element in list)
                if (element != null && ObjectEquals(key, element.Item1))
                    return element.Item2;
            return default(V);
        }

        public static V GetFirst<V>(this List<V> v, Predicate<V> p)
        {
            foreach (var v1 in v)
                if (p(v1))
                    return v1;
            return default(V);
        }

        public static bool ContainsExactly(this string s, char c, int count = 1)
        {
            int ctr = 0;
            for (int i = 0; i < s.Length; ++i)
                if (s[i] == c && ++ctr > count)
                    return false;
            return ctr == count;
        }

        public static bool ObjectEquals(object o1, object o2) => (o1==null && o2==null) || (o1!=null && o1.Equals(o2));

        public static int CollectiveLength<T>(this Tuple<string, T>[] t, bool redundant = true)
        {
            int len = 0;
            foreach (var val in t)
                len += val?.Item1?.Length ?? 0;
            return len;
        }

        public static int CollectiveLength<T>(this Tuple<T, string>[] t)
        {
            int len = 0;
            foreach (var val in t)
                len += val?.Item2?.Length ?? 0;
            return len;
        }

        public static List<T> Filter<T>(this List<T> t, Predicate<T> p)
        {
            List<T> l1 = new List<T>();
            foreach (var l in t)
                if (p(l))
                    l1.Add(l);
            return l1;
        }

        public delegate T Transformation<T, V>(V v);
        public static List<T> Transform<T, V>(this List<V> l, Transformation<T, V> t)
        {
            List<T> l1 = new List<T>();
            foreach (var l2 in l)
                l1.Add(t(l2));
            return l1;
        }
        public static T[] Transform<T, V>(this V[] l, Transformation<T, V> t)
        {
            T[] l1 = new T[l.Length];
            for (int i = 0; i < l.Length; ++i)
                l1[i] = t(l[i]);
            return l1;
        }
    }
}
