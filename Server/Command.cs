using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Server
{
    public sealed class Command
    {
        public string Name { get; }
        public Action<Command, List<Tuple<string, Parameter>>> OnInvoke { get; set; }
        private List<Parameter> parameters = new List<Parameter>();

        public string CommandString
        {
            get
            {
                StringBuilder builder = new StringBuilder(Name);
                foreach (var p in parameters)
                {
                    builder.Append(' ');
                    if (p.optional) builder.Append('{');
                    builder.Append('-').Append(p.flag);
                    if (p.type != Parameter.ParamType.NONE) builder.Append(' ').Append(p.name);
                    if (p.optional) builder.Append('}');
                }
                return builder.ToString();
            }
        }


        public Command(string name) => Name = name;

        public Command WithParameter(string pName, char flag, Parameter.ParamType type, bool optional = false)
        {
            if (GetByFlag(flag) != null) throw new Exception("Cannot have two parameters with the same flag");
            parameters.Add(new Parameter(pName, flag, type, optional));
            return this;
        }

        public Command WithParameter(Parameter parameter)
        {
            if (GetByFlag(parameter.flag) != null) throw new Exception("Cannot have two parameters with the same flag");
            parameters.Add(parameter);
            return this;
        }

        public Command SetAction(Action<Command, List<Tuple<string, Parameter>>> action)
        {
            OnInvoke = action;
            return this;
        }
        public Command SetAction(Action a) => SetAction((_, __) => a?.Invoke());

        public bool Matches(string cmd) => cmd.Split(' ')[0].EqualsIgnoreCase(Name);

        public Parameter? GetByFlag(char flag)
        {
            foreach (var param in parameters)
                if (param.flag == flag)
                    return param;
            return null;
        }

        public bool Invoke(string cmd)
        {
            if (!Matches(cmd)) return false;
            string[] parts = cmd.Split(' ');
            List<Tuple<string, Parameter>> p = new List<Tuple<string, Parameter>>();
            StringBuilder reconstruct = new StringBuilder();
            Parameter? p1 = null;
            bool wasFlag = true;
            for (int i = 1; i<parts.Length; ++i)
            {
                if (parts[i].Length == 0) continue;
                if (parts[i].StartsWith("-") && parts[i].Length==2)
                {
                    if (reconstruct.Length != 0)
                    {
                        if (
                            p1 == null ||
                            (p1.Value.type == Parameter.ParamType.NUMBER && !double.TryParse(reconstruct.ToString(), out double _)) ||
                            (p1.Value.type == Parameter.ParamType.BOOLEAN && !bool.TryParse(reconstruct.ToString(), out bool _))
                            )
                        {
                            ShowError();
                            return true;
                        }
                        p.Add(new Tuple<string, Parameter>(reconstruct.ToString(), p1.Value));
                        reconstruct.Length = 0;
                        wasFlag = true;
                    }
                    if((p1 = GetByFlag(parts[i][1])) == null)
                    {
                        ShowError();
                        return false;
                    }
                }
                else
                {
                    if(p1!=null && p1.Value.type == Parameter.ParamType.NONE)
                    {
                        ShowError();
                        return false;
                    }
                    if (!wasFlag) reconstruct.Append(' ');
                    reconstruct.Append(parts[i]);
                    wasFlag = false;
                }
            }

            if (reconstruct.Length != 0 || (p1 != null && !p.HasFlag(p1.Value.flag)))
            {
                if (
                    p1 == null ||
                    (p1.Value.type == Parameter.ParamType.NUMBER && !double.TryParse(reconstruct.ToString(), out double _)) ||
                    (p1.Value.type == Parameter.ParamType.BOOLEAN && !bool.TryParse(reconstruct.ToString(), out bool _))
                    )
                {
                    ShowError();
                    return true;
                }
                p.Add(new Tuple<string, Parameter>(reconstruct.ToString(), p1.Value));
                reconstruct.Length = 0;
            }

            foreach (var check in parameters)
                if (check.optional) continue;
                else
                {
                    foreach (var check1 in p)
                        if (check1.Item2.Equals(check))
                            goto found;
                    // Could not find a match for a required parameter
                    ShowError();
                    return false;

                    found: { }
                }
            OnInvoke?.Invoke(this, p);
            return true;
        }

        public void ShowError() => Output.Error($"Usage: {CommandString}", true, false);
    }

    public static class Commands
    {
        public static string GetFlag(this List<Tuple<string, Parameter>> l, char flag)
        {
            foreach (var flagcheck in l)
                if (flagcheck.Item2.flag == flag)
                    return flagcheck.Item1;
            return null;
        }

        public static bool HasFlag(this List<Tuple<string, Parameter>> l, char flag)
        {
            foreach (var flagcheck in l)
                if (flagcheck.Item2.flag == flag)
                    return true;
            return false;
        }
    }
}
