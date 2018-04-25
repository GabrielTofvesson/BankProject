using Client.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Client.ConsoleForms
{
    public sealed class LangManager
    {
        private const string MAPPING_PREFIX = "@string/";

        public static readonly LangManager NO_LANG = new LangManager(true);

        private readonly Dictionary<string, string> mappings;
        public string Name { get; }


        private LangManager(bool b)
        {
            mappings = new Dictionary<string, string>();
            Name = "";
        }

        public LangManager(string langName = "en_US", bool fromResource = true)
        {
            string from = null;
            if (fromResource)
            {
                PropertyInfo[] properties = typeof(Resources).GetProperties(BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var prop in properties)
                    if (prop.Name.Equals("strings_lang_" + langName) && prop.PropertyType.Equals(typeof(string)))
                    {
                        from = (string)prop.GetValue(null);
                        break;
                    }
                if (from == null)
                {
                    mappings = new Dictionary<string, string>();
                    Name = "";
                    return;
                }
            }
            else from = langName;
            mappings = DoMapping(from, out string name);
            Name = name;
        }

        public bool HasMapping(string name) => name.StartsWith(MAPPING_PREFIX) && mappings.ContainsKey(StripPrefix(name));
        public string GetMapping(string name) => mappings[StripPrefix(name)];
        public string MapIfExists(string name) => HasMapping(name) ? GetMapping(name) : name;
        private string StripPrefix(string from) => from.Substring(MAPPING_PREFIX.Length);

        private Dictionary<string, string> DoMapping(string xml, out string label)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);
            XmlNode lang = doc.GetElementsByTagName("Strings").Item(0);
            
            // Janky way of setting label since the compiler apparently doesn't accept it in an else-statement
            label = null;
            if (lang is XmlElement lang_el && lang_el.HasAttribute("label")) label = lang_el.GetAttribute("label");

            // Read language mappings
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach(var node in lang.ChildNodes)
                if(node is XmlElement el && el.Name.Equals("Entry") && el.HasAttribute("name"))
                    map.Add(el.GetAttribute("name"), el.InnerText);
            return map;
        }

        public static LangManager LoadLang(string langMeta = "strings_meta", bool fromResource = true)
        {
            if (fromResource)
            {
                PropertyInfo[] properties = typeof(Resources).GetProperties(BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var prop in properties)
                    if (prop.Name.Equals(langMeta) && prop.PropertyType.Equals(typeof(string)))
                        return new LangManager(ProcessMeta((string)prop.GetValue(null)));
            }
            return new LangManager(ProcessMeta(langMeta));
        }

        private static string ProcessMeta(string metaString)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(metaString);
            XmlNode lang = doc.GetElementsByTagName("Strings").Item(0);
            List<XmlElement> priorities = new List<XmlElement>();
            foreach(var node in lang.ChildNodes)
                if (node is XmlElement el)
                {
                    //if (el.Name.Equals("Default")) priorities.Insert(0, (XmlElement)node);
                    /*else*/ if (el.Name.Equals(/*"Fallback"*/"Lang"))
                    {
                        if (priorities.Count == 0) priorities.Add(el);
                        else
                            for (int i = 0; i < priorities.Count; ++i)
                                if (/*!priorities[i].Name.Equals("Default") && */ComparePriority(el, priorities[i]))
                                {
                                    priorities.Insert(i, el);
                                    break;
                                }
                                else if (i == priorities.Count - 1)
                                {
                                    priorities.Add(el);
                                    break;
                                }
                    }
                }

            PropertyInfo[] properties = typeof(Resources).GetProperties(BindingFlags.NonPublic | BindingFlags.Static);

            // Check if we can use system language
            string culture = System.Globalization.CultureInfo.InstalledUICulture.Name.Replace('-', '_');
            foreach(var elt in priorities)
                if (elt.InnerText.Equals(culture))
                {
                    foreach (var prop in properties)
                        if (prop.Name.Equals("strings_lang_" + culture) && prop.PropertyType.Equals(typeof(string)))
                            return culture;

                    priorities.Remove(elt);
                    break;
                }

            // Use defaults and fallbacks
            for (int i = 0; i<priorities.Count; ++i)
            {
                foreach (var prop in properties)
                    if (prop.Name.Equals("strings_lang_"+priorities[i].InnerText) && prop.PropertyType.Equals(typeof(string)))
                        return priorities[i].InnerText;
            }
            return "";
        }

        private static bool ComparePriority(XmlElement el1, XmlElement el2)
            => el1.HasAttribute("priority") && int.TryParse(el1.GetAttribute("priority"), out int el1_prio) && (!el2.HasAttribute("priority") || !int.TryParse(el2.GetAttribute("priority"), out int el2_prio) || el1_prio < el2_prio);
    }
}
