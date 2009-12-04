using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

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
					Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config");

			ConfigDirectory = Path.Combine (ConfigDirectory, "MonoDebugger");
			ConfigDirectory += Path.DirectorySeparatorChar;

			module_groups = Hashtable.Synchronized (new Hashtable ());
			directory_maps = new Dictionary<string,string> ();
			CreateDefaultModuleGroups ();
		}

		internal static XmlDocument CreateXmlDocument ()
		{
			XmlDocument doc = new XmlDocument ();
			doc.LoadXml ("<?xml version=\"1.0\"?>\n" +
				     "<DebuggerConfiguration fileversion = \"1.0\" />");
			return doc;
		}

		public bool LoadConfiguration()
		{
			try {
				if (!Directory.Exists (ConfigDirectory))
					Directory.CreateDirectory (ConfigDirectory);

				LoadConfigurationFromStream (Path.Combine (ConfigDirectory, ConfigFileName));
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

		public void SetupXSP ()
		{
			stay_in_thread = false;
			broken_threading = true;
			load_native_symtabs = false;
			follow_fork = false;
			notify_thread_creation = false;
			hide_auto_generated = true;
			is_xsp = true;
		}

		public void SetupCLI ()
		{
			is_cli = true;
		}

		void LoadConfigurationFromStream (string filename)
		{
			if (File.Exists (filename)) {
				using (FileStream stream = new FileStream (filename, FileMode.Open))
					LoadConfigurationFromStream (stream);
			}
		}

		void LoadConfigurationFromStream (Stream stream)
		{
			XmlReaderSettings settings = new XmlReaderSettings ();
			Assembly ass = Assembly.GetExecutingAssembly ();
			using (Stream schema = ass.GetManifestResourceStream ("DebuggerConfiguration"))
				settings.Schemas.Add (null, new XmlTextReader (schema));

			XmlReader reader = XmlReader.Create (stream, settings);

			XPathDocument doc = new XPathDocument (reader);
			XPathNavigator nav = doc.CreateNavigator ();

			XPathNodeIterator iter = nav.Select ("/DebuggerConfiguration/Configuration/*");
			while (iter.MoveNext ()) {
				if (iter.Current.Name == "LoadNativeSymtabs")
					LoadNativeSymtabs = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "BrokenThreading")
					BrokenThreading = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "StayInThread")
					StayInThread = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "FollowFork")
					FollowFork = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "OpaqueFileNames")
					OpaqueFileNames = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "StopOnManagedSignals")
					StopOnManagedSignals = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "NestedBreakStates")
					NestedBreakStates = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "RedirectOutput")
					RedirectOutput = Boolean.Parse (iter.Current.Value);
				else if (iter.Current.Name == "Martin_Boston_07102008")
					Private_Martin_Boston_07102008 = Boolean.Parse (iter.Current.Value);
				else
					throw new InvalidOperationException ();
			}

			iter = nav.Select ("/DebuggerConfiguration/ModuleGroups/ModuleGroup");
			while (iter.MoveNext ()) {
				string name = iter.Current.GetAttribute ("name", "");
				ModuleGroup group = CreateModuleGroup (name);

				group.SetSessionData (iter);
			}

			iter = nav.Select ("/DebuggerConfiguration/DirectoryMap/Map");
			while (iter.MoveNext ()) {
				string from = iter.Current.GetAttribute ("from", "");
				string to = iter.Current.GetAttribute ("to", "");
				directory_maps.Add (from, to);
			}
		}

		void SaveConfigurationToStream (string filename)
		{
			using (FileStream stream = new FileStream (filename, FileMode.Create)) {
				XmlDocument doc = CreateXmlDocument ();

				XmlElement element = doc.CreateElement ("Configuration");
				doc.DocumentElement.AppendChild (element);

				XmlElement load_native_symtabs_e = doc.CreateElement ("LoadNativeSymtabs");
				load_native_symtabs_e.InnerText = LoadNativeSymtabs ? "true" : "false";
				element.AppendChild (load_native_symtabs_e);

				XmlElement broken_threading_e = doc.CreateElement ("BrokenThreading");
				broken_threading_e.InnerText = BrokenThreading ? "true" : "false";
				element.AppendChild (broken_threading_e);

				XmlElement stay_in_thread_e = doc.CreateElement ("StayInThread");
				stay_in_thread_e.InnerText = StayInThread ? "true" : "false";
				element.AppendChild (stay_in_thread_e);

				XmlElement follow_fork_e = doc.CreateElement ("FollowFork");
				follow_fork_e.InnerText = FollowFork ? "true" : "false";
				element.AppendChild (follow_fork_e);

				XmlElement opaque_file_names_e = doc.CreateElement ("OpaqueFileNames");
				opaque_file_names_e.InnerText = OpaqueFileNames ? "true" : "false";
				element.AppendChild (opaque_file_names_e);

				XmlElement stop_on_signals_e = doc.CreateElement ("StopOnManagedSignals");
				stop_on_signals_e.InnerText = StopOnManagedSignals ? "true" : "false";
				element.AppendChild (stop_on_signals_e);

				XmlElement nested_break_states_e = doc.CreateElement ("NestedBreakStates");
				nested_break_states_e.InnerText = NestedBreakStates ? "true" : "false";
				element.AppendChild (nested_break_states_e);

				XmlElement redirect_output_e = doc.CreateElement ("RedirectOutput");
				redirect_output_e.InnerText = RedirectOutput ? "true" : "false";
				element.AppendChild (redirect_output_e);

				XmlElement module_groups = doc.CreateElement ("ModuleGroups");
				doc.DocumentElement.AppendChild (module_groups);

				foreach (ModuleGroup group in ModuleGroups)
					group.GetSessionData (module_groups);

				doc.Save (stream);
			}
		}

		bool stay_in_thread = false;
		bool broken_threading = true;
		bool load_native_symtabs = false;
		bool follow_fork = false;
		bool notify_thread_creation = true;
		bool hide_auto_generated = false;
		bool opaque_file_names = false;
		bool stop_on_managed_signals = true;
		bool nested_break_states = false;
		bool redirect_output = false;
		bool is_xsp = false;
		bool is_cli = false;
		Hashtable module_groups;
		Dictionary<string,string> directory_maps;

		//
		// Module groups
		//

		private void CreateDefaultModuleGroups ()
		{
			module_groups.Add ("native", new ModuleGroup ("native", true, false, false));
			module_groups.Add ("dll", new ModuleGroup ("dll", false, true, false));
			module_groups.Add ("runtime", new ModuleGroup ("runtime", false, true, false));
			module_groups.Add ("managed", new ModuleGroup ("managed", false, true, true));
			module_groups.Add ("user", new ModuleGroup ("user", false, true, true));
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
			set { load_native_symtabs = value; }
		}

		public bool StayInThread {
			get { return stay_in_thread; }
			set { stay_in_thread = value; }
		}

		public bool BrokenThreading {
			get { return broken_threading; }
			set { broken_threading = value; }
		}

		public bool FollowFork {
			get { return follow_fork; }
			set { follow_fork = value; }
		}

		public bool OpaqueFileNames {
			get { return opaque_file_names; }
			set { opaque_file_names = value; }
		}

		public bool StopOnManagedSignals {
			get { return stop_on_managed_signals; }
			set { stop_on_managed_signals = value; }
		}

		public bool NestedBreakStates {
			get { return nested_break_states; }
			set { nested_break_states = value; }
		}

		public bool RedirectOutput {
			get { return redirect_output; }
			set { redirect_output = value; }
		}

		public bool Private_Martin_Boston_07102008 {
			get; set;
		}

		/*
		 * Whether or not to notify the user when new threads have
		 * been created / threads exited.
		 */
		public bool NotifyUser_ThreadCreation {
			get { return notify_thread_creation; }
			set { notify_thread_creation = value; }
		}

		public bool HideAutoGenerated {
			get { return hide_auto_generated; }
		}

		public bool IsCLI {
			get { return is_cli; }
		}

		public bool IsXSP {
			get { return is_xsp; }
		}

		public Dictionary<string,string> DirectoryMaps {
			get { return directory_maps; }
		}

		public static string WindowsToUnix (string path)
		{
			path = path.Replace ('\\', '/');
			return path;
		}

		public string PrintConfiguration (bool expert_mode)
		{
			StringBuilder sb = new StringBuilder ("Debugger Configuration:\n");
			sb.Append (String.Format ("  Load native symtabs (native-symtabs):               {0}\n",
						  LoadNativeSymtabs ? "yes" : "no"));
			sb.Append (String.Format ("  Follow fork (follow-fork):                          {0}\n",
						  FollowFork ? "yes" : "no"));
			sb.Append (String.Format ("  Stop on managed signals (stop-on-managed-signals):  {0}\n",
						  StopOnManagedSignals ? "yes" : "no"));
			sb.Append (String.Format ("  Enable nested break states (nested-break-states):   {0}\n",
						  NestedBreakStates ? "yes" : "no"));
			sb.Append (String.Format ("  Redirect output (redirect-output):                  {0}\n",
						  RedirectOutput ? "yes" : "no"));

			if (expert_mode) {
				sb.Append ("\nExpert Settings:\n");
				sb.Append (String.Format ("  Broken threading (broken-threading):   {0}\n",
							  BrokenThreading ? "enabled" : "disabled"));
				sb.Append (String.Format ("  Stay in thread (stay-in-thread):       {0}\n",
							  BrokenThreading ? "yes" : "no"));
			}
			return sb.ToString ();
		}
	}
}
