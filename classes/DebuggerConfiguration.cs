using System;
using System.IO;
using System.Data;
using System.Reflection;
using System.Collections;
using System.Configuration;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Mono.Debugger
{
	public class DebuggerConfiguration : DebuggerMarshalByRefObject
	{
		internal readonly string ConfigDirectory;

		const string ConfigFileName = "MonoDebugger.xml";
		
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

		internal static DataSet CreateDataSet ()
		{
			DataSet ds = new DataSet ("DebuggerSession");

			Assembly ass = Assembly.GetExecutingAssembly ();
			using (Stream schema = ass.GetManifestResourceStream ("DebuggerConfiguration"))
				ds.ReadXmlSchema (schema);

			return ds;
		}

		public bool LoadConfiguration()
		{
			try {
				if (!Directory.Exists (ConfigDirectory))
					Directory.CreateDirectory (ConfigDirectory);

				LoadConfigurationFromStream (ConfigDirectory + ConfigFileName);
				return true;
			} catch {
				return false;
			}
		}

		public bool SaveConfiguration ()
		{
			try {
				if (!Directory.Exists (ConfigDirectory))
					Directory.CreateDirectory (ConfigDirectory);

				SaveConfigurationToStream (ConfigDirectory + ConfigFileName);
				return true;
			} catch {
				return false;
			}
		}

		void LoadConfigurationFromStream (string filename)
		{
			DataSet ds = CreateDataSet ();
			ds.ReadXml (filename, XmlReadMode.IgnoreSchema);

			DataRow config_row = ds.Tables ["Configuration"].Rows [0];
			if (!config_row.IsNull ("load-native-symtabs"))
				load_native_symtabs = (bool) config_row ["load-native-symtabs"];

			DataTable group_table = ds.Tables ["ModuleGroup"];
			foreach (DataRow row in group_table.Rows) {
				ModuleGroup group = CreateModuleGroup ((string) row ["name"]);
				group.SetSessionData (row);
			}
		}

		void SaveConfigurationToStream (string filename)
		{
			DataSet ds = CreateDataSet ();

			DataTable config_table = ds.Tables ["Configuration"];
			DataRow config_row = config_table.NewRow ();
			config_row ["load-native-symtabs"] = true;
			config_table.Rows.Add (config_row);

			DataTable group_table = ds.Tables ["ModuleGroup"];
			foreach (ModuleGroup group in ModuleGroups) {
				DataRow row = group_table.NewRow ();
				group.GetSessionData (row);
				group_table.Rows.Add (row);
			}

			ds.WriteXml (filename);
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

		internal ModuleGroup CreateModuleGroup (string name)
		{
			lock (module_groups.SyncRoot) {
				ModuleGroup group = (ModuleGroup) module_groups [name];
				if (group == null) {
					group = new ModuleGroup (name);
					module_groups.Add (name, group);
				}
				return group;
			}
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

		public bool LoadNativeSymtabs {
			get { return load_native_symtabs; }
			set { load_native_symtabs = true; }
		}

		public bool Test {
			get { return false; }
		}
	}
}
