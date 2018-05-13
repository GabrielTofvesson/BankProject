using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    // Enables mutability for immutable values or parameters
    public sealed class Proxy<T>
    {
        public T Value { get; set; }

        public Proxy(T initial = default(T)) => Value = initial;

        public static implicit operator T(Proxy<T> p) => p.Value;
        public static implicit operator Proxy<T>(T t) => new Proxy<T>(t);
    }
}
