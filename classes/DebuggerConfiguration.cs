using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Configuration;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Mono.Debugger
{
	public enum ConfigurationSection
	{
		General
	}

	public class ConfigurationItem : Attribute
	{
		public readonly ConfigurationSection Section;
		public readonly string Description;

		public ConfigurationItem (ConfigurationSection section, string description)
		{
			this.Section = section;
			this.Description = description;
		}
	}

	public class DebuggerConfiguration : DebuggerMarshalByRefObject
	{
		internal readonly string ConfigDirectory;

		const string ConfigFileName = "MonoDebugger.xml";
		const string ConfigFileVersion = "1.0";

		const string ConfigXmlRootNodeName = "MonoDebuggerConfiguration";
		
		public DebuggerConfiguration ()
		{
			ConfigDirectory = Environment.GetEnvironmentVariable ("XDG_CONFIG_HOME");
			if ((ConfigDirectory == null) || (ConfigDirectory == ""))
				ConfigDirectory = Path.Combine (
					Environment.GetEnvironmentVariable ("HOME"), ".config");

			ConfigDirectory = Path.Combine (ConfigDirectory, "MonoDebugger");
			ConfigDirectory += Path.DirectorySeparatorChar;

			module_groups = Hashtable.Synchronized (new Hashtable ());
			CreateDefaultModuleGroups ();
		}

		public bool LoadConfiguration()
		{
			try {
				if (!Directory.Exists (ConfigDirectory))
					Directory.CreateDirectory (ConfigDirectory);

				return LoadConfigurationFromStream (ConfigDirectory + ConfigFileName);
			} catch {
				return false;
			}
		}

		public bool SaveConfiguration ()
		{
			try {
				if (!Directory.Exists (ConfigDirectory))
					Directory.CreateDirectory (ConfigDirectory);

				return SaveConfigurationToStream (ConfigDirectory + ConfigFileName);
			} catch {
				return false;
			}
		}

		bool LoadConfigurationFromStream (string filename)
		{
			XmlDocument doc = new XmlDocument ();
			doc.Load (filename);

			if (doc.DocumentElement.Attributes ["fileversion"].InnerText != ConfigFileVersion)
				return false;

			if (!LoadConfigurationFromXml (doc))
				return false;
			if (!LoadModuleGroupsFromXml (doc))
				return false;

			return true;
		}

		bool SaveConfigurationToStream (string filename)
		{
			XmlDocument doc = new XmlDocument ();
			doc.LoadXml ("<?xml version=\"1.0\"?>\n<" + ConfigXmlRootNodeName +
				     " fileversion = \"" + ConfigFileVersion + "\" />");
			
			doc.DocumentElement.AppendChild (SaveConfigurationToXml (doc));
			doc.DocumentElement.AppendChild (SaveModuleGroupsToXml (doc));
			doc.Save (filename);
			return true;
		}

		XmlElement SaveConfigurationToXml (XmlDocument doc)
		{
			XmlElement root = doc.CreateElement ("Configuration");

			Hashtable sections = new Hashtable ();
			foreach (string section in Enum.GetNames (typeof (ConfigurationSection))) {
				XmlElement element = doc.CreateElement ("Section");
				root.AppendChild (element);
				sections.Add (section, element);

				XmlAttribute name = doc.CreateAttribute ("name");
				name.InnerText = section;
				element.Attributes.Append (name);
			}

			PropertyInfo[] props = typeof (DebuggerConfiguration).GetProperties (
				BindingFlags.Public | BindingFlags.Instance);

			foreach (PropertyInfo prop in props) {
				object[] attrs = prop.GetCustomAttributes (typeof (ConfigurationItem), true);
				if (attrs.Length != 1)
					continue;

				ConfigurationItem item = (ConfigurationItem) attrs [0];
				XmlElement section = (XmlElement) sections [item.Section.ToString ()];

				string value = prop.GetValue (this, null).ToString ();
				XmlElement element = AddProperty (doc, prop.Name, item.Description, value);
				section.AppendChild (element);
			}

			return root;
		}

		XmlElement AddProperty (XmlDocument doc, string name, string description, string value)
		{
			XmlElement element = doc.CreateElement ("Property");

			XmlAttribute key = doc.CreateAttribute ("key");
			key.InnerText = name;
			element.Attributes.Append (key);

			XmlAttribute value_attr = doc.CreateAttribute ("value");
			value_attr.InnerText = value;
			element.Attributes.Append (value_attr);

			if (description != null) {
				XmlAttribute desc = doc.CreateAttribute ("description");
				desc.InnerText = description;
				element.Attributes.Append (desc);
			}

			return element;
		}

		XmlElement AddProperty (XmlDocument doc, string name, object value)
		{
			return AddProperty (doc, name, null, value.ToString ());
		}

		XmlElement SaveModuleGroupsToXml (XmlDocument doc)
		{
			XmlElement root = doc.CreateElement ("ModuleGroups");

			foreach (ModuleGroup group in module_groups.Values) {
				XmlElement element = doc.CreateElement ("ModuleGroup");
				root.AppendChild (element);

				XmlAttribute name = doc.CreateAttribute ("name");
				name.InnerText = group.Name;
				element.Attributes.Append (name);

				element.AppendChild (AddProperty (doc, "HideFromUser", group.HideFromUser));
				element.AppendChild (AddProperty (doc, "LoadSymbols", group.LoadSymbols));
				element.AppendChild (AddProperty (doc, "StepInto", group.StepInto));
				if (group.Regexp != null)
					element.AppendChild (AddProperty (doc, "Regexp", group.Regexp));
			}

			return root;
		}

		bool LoadConfigurationFromXml (XmlDocument doc)
		{
			XmlNodeList list = doc.GetElementsByTagName ("Configuration");
			if (list.Count != 1)
				return false;

			XmlNode root = list [0];

			foreach (XmlElement section in root.ChildNodes) {
				if (section.Name != "Section")
					continue;

				XmlNodeList nodes = section.ChildNodes;
				foreach (XmlElement element in nodes) {
					if (element.Name != "Property")
						continue;

					XmlAttribute name = element.Attributes ["key"];
					if (name == null)
						return false;

					PropertyInfo prop = typeof (DebuggerConfiguration).GetProperty (
						name.InnerText, BindingFlags.Public | BindingFlags.Instance);
					if (prop == null)
						return false;

					Type item_type = typeof (ConfigurationItem);
					if (prop.GetCustomAttributes (item_type, true).Length != 1)
						return false;

					XmlAttribute value_attr = element.Attributes ["value"];
					if (value_attr == null)
						return false;

					object value;
					string text = value_attr.InnerText;
					if (prop.PropertyType == typeof (bool)) {
						value = Boolean.Parse (text);
					} else {
						return false;
					}

					prop.SetValue (this, value, null);
				}
			}

			return true;
		}

		bool LoadModuleGroupsFromXml (XmlDocument doc)
		{
			XmlNodeList list = doc.GetElementsByTagName ("ModuleGroups");
			if (list.Count != 1)
				return false;

			XmlNode root = list [0];

			foreach (XmlElement xml_group in root.ChildNodes) {
				if (xml_group.Name != "ModuleGroup")
					continue;

				XmlAttribute name_attr = xml_group.Attributes ["name"];
				if (name_attr == null)
					return false;

				ModuleGroup group = GetModuleGroup (name_attr.InnerText);
				if (group == null) {
					group = new ModuleGroup (name_attr.InnerText);
					module_groups.Add (name_attr.InnerText, group);
				}

				XmlNodeList nodes = xml_group.ChildNodes;
				foreach (XmlElement element in nodes) {
					if (element.Name != "Property")
						continue;

					XmlAttribute name = element.Attributes ["key"];
					XmlAttribute value = element.Attributes ["value"];
					if ((name == null) || (value == null))
						return false;

					switch (name.InnerText) {
					case "HideFromUser":
						group.HideFromUser = Boolean.Parse (value.InnerText);
						break;

					case "LoadSymbols":
						group.LoadSymbols = Boolean.Parse (value.InnerText);
						break;

					case "StepInto":
						group.StepInto = Boolean.Parse (value.InnerText);
						break;

					case "Regexp":
						group.Regexp = value.InnerText;
						break;

					default:
						return false;
					}
				}
			}

			return true;
		}

		bool load_native_symtabs;
		Hashtable module_groups;

		//
		// Module groups
		//

		private void CreateDefaultModuleGroups ()
		{
			module_groups.Add ("native", new ModuleGroup ("native", true, false, false));
			module_groups.Add ("dll", new ModuleGroup ("dll", false, true, false));
			module_groups.Add ("runtime", new ModuleGroup ("runtime", false, true, false));
			module_groups.Add ("managed", new ModuleGroup ("managed", false, true, true));
			module_groups.Add ("corlib", new ModuleGroup ("corlib", false, true, true));
		}

		public ModuleGroup GetModuleGroup (string name)
		{
			return (ModuleGroup) module_groups [name];
		}

		internal ModuleGroup[] ModuleGroups {
			get {
				lock (module_groups.SyncRoot) {
					ModuleGroup[] groups = new ModuleGroup [module_groups.Count];
					module_groups.Values.CopyTo (groups, 0);
					return groups;
				}
			}
		}

		internal ModuleGroup GetModuleGroup (SymbolFile symfile)
		{
			foreach (ModuleGroup group in module_groups.Values) {
				if (group.Regexp == null)
					continue;

				try {
					if (Regex.IsMatch (symfile.FullName, group.Regexp))
						return group;
				} catch {
				}
			}

			if (symfile.IsNative) {
				if (symfile.FullName.EndsWith ("/mono"))
					return GetModuleGroup ("runtime");
				else if (symfile.HasDebuggingInfo)
					return GetModuleGroup ("dll");
				else
					return GetModuleGroup ("native");
			} else {
				string assembly_name = symfile.FullName;
				int pos = assembly_name.IndexOf (',');
				if (pos > 0)
					assembly_name = assembly_name.Substring (0, pos);

				string[] corlib_assemblies = { "mscorlib", "System" };

				foreach (string name in corlib_assemblies) {
					if (assembly_name == name)
						return GetModuleGroup ("corlib");
				}

				return GetModuleGroup ("managed");
			}
		}

		//
		// Debugger Configuration
		//

		[ConfigurationItem (ConfigurationSection.General,
				    "Load native symbol tables when debugging managed code.")]
		public bool LoadNativeSymtabs {
			get { return load_native_symtabs; }
			set { load_native_symtabs = true; }
		}

		public bool Test {
			get { return false; }
		}
	}
}
