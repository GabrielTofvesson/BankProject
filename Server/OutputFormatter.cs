using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public sealed class OutputFormatter
    {
        private readonly List<Tuple<string, string>> lines = new List<Tuple<string, string>>();
        private int leftLen = 0;
        private readonly int minPad;
        private readonly string prepend, delimiter, postpad, trail;


        public OutputFormatter(int minPad = 1, string prepend = "", string delimiter = "", string postpad = "", string trail = "")
        {
            this.prepend = prepend;
            this.delimiter = delimiter;
            this.postpad = postpad;
            this.trail = trail;
            this.minPad = Math.Abs(minPad);
        }

        public OutputFormatter Append(string key, string value)
        {
            lines.Add(new Tuple<string, string>(key, value));
            leftLen = Math.Max(key.Length + minPad, leftLen);
            return this;
        }

        public string GetString()
        {
            StringBuilder builder = new StringBuilder();
            foreach (var line in lines)
                builder
                    .Append(prepend)
                    .Append(line.Item1)
                    .Append(delimiter)
                    .Append(Pad(line.Item1, leftLen))
                    .Append(postpad)
                    .Append(line.Item2)
                    .Append(trail)
                    .Append('\n');
            builder.Length -= 1;
            return builder.ToString();
        }

        private static string Pad(string msg, int length)
        {
            if (msg.Length >= length) return "";
            char[] c = new char[length - msg.Length];
            for (int i = 0; i < c.Length; ++i) c[i] = ' ';
            return new string(c);
        }
    }
}
