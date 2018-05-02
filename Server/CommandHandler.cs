using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public sealed class CommandHandler
    {
        private readonly List<Tuple<Command, string>> commands = new List<Tuple<Command, string>>();
        private int leftLen = 0;
        private readonly int minPad;
        private readonly string prepend, delimiter, postpad, trail;


        public CommandHandler(int minPad = 1, string prepend = "", string delimiter = "", string postpad = "", string trail = "")
        {
            this.prepend = prepend;
            this.delimiter = delimiter;
            this.postpad = postpad;
            this.trail = trail;
            this.minPad = Math.Abs(minPad);
        }

        public CommandHandler Append(Command c, string description)
        {
            commands.Add(new Tuple<Command, string>(c, description));
            leftLen = Math.Max(c.CommandString.Length + minPad, leftLen);
            return this;
        }

        public bool HandleCommand(string cmd)
        {
            foreach (var command in commands)
                if (command.Item1.Invoke(cmd))
                    return true;
            return false;
        }

        public string GetString()
        {
            StringBuilder builder = new StringBuilder();
            string cache;
            foreach (var command in commands)
                builder
                    .Append(prepend)
                    .Append(cache = command.Item1.CommandString)
                    .Append(delimiter)
                    .Append(Pad(cache, leftLen))
                    .Append(postpad)
                    .Append(command.Item2)
                    .Append(trail)
                    .Append('\n');
            if(commands.Count > 0) builder.Length -= 1;
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
