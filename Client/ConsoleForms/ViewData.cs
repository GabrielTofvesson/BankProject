using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms.Parameters
{
    public sealed class ViewData
    {
        public delegate string TransformAction(ViewData rawValue);

        public string Name { get; }
        public string InnerText { get; }
        public readonly Dictionary<string, string> attributes = new Dictionary<string, string>();
        public readonly List<ViewData> nestedData = new List<ViewData>();

        public ViewData(string name, string innerText = "")
        {
            Name = (name ?? "").Replace("\r", "");
            InnerText = (innerText ?? "").Replace("\r", "");
        }

        public ViewData Get(string name)
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(name))
                    return data;
            return null;
        }

        public int TextAsInt(int def = default(int)) => int.TryParse(InnerText, out int p) ? p : def;
        public int AttribueAsInt(string name, int def = default(int)) => attributes.ContainsKey(name) && int.TryParse(attributes[name], out int p) ? p : def;
        public bool AttribueAsBool(string name, bool def = default(bool)) => attributes.ContainsKey(name) && bool.TryParse(attributes[name], out bool p) ? p : def;
        public Tuple<string, string>[] CollectSub(string name, TransformAction action = null)
        {
            List<Tuple<string, string>> l = new List<Tuple<string, string>>();
            foreach (var data in nestedData)
                if (data.Name.Equals(name))
                    l.Add(new Tuple<string, string>(data.InnerText, action?.Invoke(data) ?? ""));
            return l.ToArray();
        }
        public string NestedText(string nestedDataName, string def = "")
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(nestedDataName))
                    return data.InnerText;
            return def;
        }
        public int NestedInt(string nestedDataName, int def = default(int))
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(nestedDataName) && int.TryParse(data.InnerText, out int p))
                    return p;
            return def;
        }
        public int NestedAttribute(string nestedName, string attributeName, int def = default(int))
        {
            foreach (var data in nestedData)
                if (data.Name.Equals(nestedName) && data.attributes.ContainsKey(attributeName) && int.TryParse(data.attributes[attributeName], out int p))
                    return p;
            return def;
        }
        public ViewData SetAttribute<T>(string attrName, T value)
        {
            attributes[attrName] = value == null ? "null" : value.ToString();
            return this;
        }
        public ViewData AddNested(ViewData nest)
        {
            nestedData.Add(nest);
            return this;
        }

        public string GetAttribute(string attr, string def = "") => attributes.ContainsKey(attr) ? attributes[attr] : def;
    }
}
