using System;
using System.IO;
using System.Reflection;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml;
using System.Xml.XPath;

using Mono.Debugger.Languages;
using Mono.Debugger.Backend;

namespace Mono.Debugger
{
	public delegate void ModulesChangedHandler (DebuggerSession session);

	public enum LocationType
	{
		Default,
		Method,
		Constructor,
		DelegateInvoke,
		PropertyGetter,
		PropertySetter,
		EventAdd,
		EventRemove
	}

	public interface IExpressionParser
	{
		SourceLocation Parse (Thread target, StackFrame frame,
				      LocationType type, string name);
	}

	[Serializable]
	public class DebuggerSession : DebuggerMarshalByRefObject
	{
		public readonly string Name;
		public readonly DebuggerConfiguration Config;
		public readonly DebuggerOptions Options;

		protected readonly Hashtable modules;
		protected readonly Hashtable events;
		protected readonly Hashtable displays;
		protected readonly Hashtable thread_groups;
		protected readonly ThreadGroup main_thread_group;

		protected readonly Dictionary<string,string> directory_maps;
		protected readonly List<string> user_module_paths;
		protected readonly List<string> user_modules;

		Process main_process;
		IExpressionParser parser;
		XmlDocument saved_session;

		private DebuggerSession (DebuggerConfiguration config, string name,
					 IExpressionParser parser)
		{
			this.Config = config;
			this.Name = name;
			this.parser = parser;

			modules = Hashtable.Synchronized (new Hashtable ());
			events = Hashtable.Synchronized (new Hashtable ());
			displays = Hashtable.Synchronized (new Hashtable ());
			thread_groups = Hashtable.Synchronized (new Hashtable ());
			main_thread_group = CreateThreadGroup ("main");
			directory_maps = new Dictionary<string,string> ();
			user_module_paths = new List<string> ();
			user_modules = new List<string> ();
		}

		public DebuggerSession (DebuggerConfiguration config, DebuggerOptions options,
					string name, IExpressionParser parser)
			: this (config, name, parser)
		{
			this.Options = options;

			if (Options.StopInMain)
				AddEvent (new MainMethodBreakpoint (this));
		}

		internal DebuggerSession Clone (DebuggerOptions new_options, string new_name)
		{
			return new DebuggerSession (Config, new_options, new_name, parser);
		}

		public void SaveSession (Stream stream)
		{
			saved_session = SaveSession ();
			saved_session.Save (stream);
		}

		protected XmlDocument SaveSession ()
		{
			XmlDocument doc = DebuggerConfiguration.CreateXmlDocument ();

			XmlElement module_groups = doc.CreateElement ("ModuleGroups");
			doc.DocumentElement.AppendChild (module_groups);

			foreach (ModuleGroup group in Config.ModuleGroups)
				group.GetSessionData (module_groups);

			XmlElement root = doc.CreateElement ("DebuggerSession");
			root.SetAttribute ("name", Name);
			doc.DocumentElement.AppendChild (root);

			XmlElement options = doc.CreateElement ("Options");
			Options.GetSessionData (options);
			root.AppendChild (options);

			XmlElement modules = root.OwnerDocument.CreateElement ("Modules");
			root.AppendChild (modules);

			foreach (Module module in Modules)
				module.GetSessionData (modules);

			XmlElement thread_groups = root.OwnerDocument.CreateElement ("ThreadGroups");
			root.AppendChild (thread_groups);

			foreach (ThreadGroup group in ThreadGroups)
				AddThreadGroup (thread_groups, group);

			XmlElement event_list = root.OwnerDocument.CreateElement ("Events");
			root.AppendChild (event_list);

			foreach (Event e in Events)
				e.GetSessionData (event_list);

			XmlElement display_list = root.OwnerDocument.CreateElement ("Displays");
			root.AppendChild (display_list);

			foreach (Display d in Displays)
				d.GetSessionData (display_list);

			return doc;
		}

		public DebuggerSession (DebuggerConfiguration config, Stream stream,
					IExpressionParser parser)
			: this (config, "main", parser)
		{
			XmlReaderSettings settings = new XmlReaderSettings ();
			Assembly ass = Assembly.GetExecutingAssembly ();
			using (Stream schema = ass.GetManifestResourceStream ("DebuggerConfiguration"))
				settings.Schemas.Add (null, new XmlTextReader (schema));

			XmlReader reader = XmlReader.Create (stream, settings);

			saved_session = new XmlDocument ();
			saved_session.Load (reader);
			reader.Close ();

			XPathNavigator nav = saved_session.CreateNavigator ();

			XPathNodeIterator session_iter = nav.Select (
				"/DebuggerConfiguration/DebuggerSession[@name='" + Name + "']");
			if (!session_iter.MoveNext ())
				throw new InternalError ();

			XPathNodeIterator options_iter = session_iter.Current.Select ("Options/*");
			Options = new DebuggerOptions (options_iter);

			XPathNodeIterator dir_map_iter = session_iter.Current.Select ("DirectoryMap/Map");
			while (dir_map_iter.MoveNext ()) {
				string from = dir_map_iter.Current.GetAttribute ("from", "");
				string to = dir_map_iter.Current.GetAttribute ("to", "");
				directory_maps.Add (from, to);
			}

			LoadSession (nav);
		}

		void AddThreadGroup (XmlElement root, ThreadGroup group)
		{
			XmlElement element = root.OwnerDocument.CreateElement ("ThreadGroup");
			element.SetAttribute ("name", group.Name);

			root.AppendChild (element);
		}

		public Event InsertBreakpoint (ThreadGroup group, SourceLocation location)
		{
			Event handle = new SourceBreakpoint (this, group, location);
			AddEvent (handle);
			return handle;
		}

		public Event InsertBreakpoint (ThreadGroup group, LocationType type, string name)
		{
			Event handle = new ExpressionBreakpoint (this, group, type, name);
			AddEvent (handle);
			return handle;
		}

		public Event InsertBreakpoint (Thread target, ThreadGroup group,
					       TargetAddress address)
		{
			Event handle = new AddressBreakpoint (address.ToString (), group, address);
			handle.Activate (target);
			AddEvent (handle);
			return handle;
		}

		[Obsolete]
		public Event InsertExceptionCatchPoint (Thread target, ThreadGroup group,
							TargetType exception)
		{
			return InsertExceptionCatchPoint (target, group, exception, false);
		}

		public Event InsertExceptionCatchPoint (Thread target, ThreadGroup group,
							TargetType exception, bool unhandled)
		{
			Event handle = new ExceptionCatchPoint (group, exception, unhandled);
			handle.Activate (target);
			AddEvent (handle);
			return handle;
		}

		public Event InsertHardwareWatchPoint (Thread target, TargetAddress address,
						       HardwareWatchType type)
		{
			Event handle = new AddressBreakpoint (type, address);
			handle.Activate (target);
			AddEvent (handle);
			return handle;
		}

		internal SourceLocation ParseLocation (Thread target, StackFrame frame,
						       LocationType type, string name)
		{
			return parser.Parse (target, frame, type, name);
		}

		public SourceFile FindFile (string filename)
		{
			if (main_process == null)
				return null;

			Module[] modules = main_process.Modules;

			foreach (Module module in modules) {
				SourceFile file = module.FindFile (filename);
				if (file != null)
					return file;
			}

			if (Config.OpaqueFileNames || Path.IsPathRooted (filename))
				return null;

			filename = Path.GetFullPath (Path.Combine (
				Options.WorkingDirectory, filename));

			foreach (Module module in modules) {
				SourceFile file = module.FindFile (filename);
				if (file != null)
					return file;
			}

			return null;
		}

		//
		// Session management.
		//

		internal void OnMainProcessCreated (Process process)
		{
			if (!process.IsManaged) {
				Config.GetModuleGroup ("dll").StepInto = true;
				Config.GetModuleGroup ("native").StepInto = true;
			}

			main_process = process;
		}

		internal void OnProcessCreated (Process process)
		{ }

		internal void OnProcessExited (Process process)
		{
			if (process != main_process)
				return;

			main_process = null;
			Event[] list = new Event [events.Count];
			events.Values.CopyTo (list, 0);

			foreach (Event e in list) {
				e.OnTargetExited ();
				if (!e.IsPersistent)
					events.Remove (e.Index);
			}
		}

		protected void LoadSession (XPathNavigator nav)
		{
			XPathNodeIterator session_iter = nav.Select (
				"/DebuggerConfiguration/DebuggerSession[@name='" + Name + "']");
			if (!session_iter.MoveNext ())
				throw new InternalError ();

			XPathNodeIterator group_iter = nav.Select (
				"/DebuggerConfiguration/ModuleGroups/ModuleGroup");
			while (group_iter.MoveNext ()) {
				string name = group_iter.Current.GetAttribute ("name", "");
				ModuleGroup group = Config.CreateModuleGroup (name);

				group.SetSessionData (group_iter);
			}

			XPathNodeIterator modules_iter = session_iter.Current.Select ("Modules/*");
			while (modules_iter.MoveNext ()) {
				string name = modules_iter.Current.GetAttribute ("name", "");
				string group = modules_iter.Current.GetAttribute ("group", "");

				Module module = (Module) modules [name];
				if (module == null) {
					ModuleGroup mgroup = Config.GetModuleGroup (group);
					module = new Module (mgroup, name, null);
					modules.Add (name, module);
				}

				module.SetSessionData (modules_iter);
			}

			XPathNodeIterator event_iter = session_iter.Current.Select ("Events/*");
			while (event_iter.MoveNext ()) {
				if (event_iter.Current.Name != "Breakpoint")
					throw new InternalError ();

				int index = Int32.Parse (event_iter.Current.GetAttribute ("index", ""));
				bool enabled = Boolean.Parse (event_iter.Current.GetAttribute ("enabled", ""));

				string gname = event_iter.Current.GetAttribute ("threadgroup", "");
				ThreadGroup group;
				if (gname == "system")
					group = ThreadGroup.System;
				else if (gname == "global")
					group = ThreadGroup.Global;
				else
					group = CreateThreadGroup (gname);

				Event e = null;

				XPathNodeIterator children = event_iter.Current.SelectChildren (
					XPathNodeType.Element);

				if (!children.MoveNext ())
					throw new InternalError ();
				e = ParseEvent (children.Current, index, group);
				if (children.MoveNext ())
					throw new InternalError ();

				e.IsEnabled = enabled;
				AddEvent (e);
			}

			XPathNodeIterator display_iter = session_iter.Current.Select ("Displays/*");
			while (display_iter.MoveNext ()) {
				if (display_iter.Current.Name != "Display")
					throw new InternalError ();

				int index = Int32.Parse (display_iter.Current.GetAttribute ("index", ""));
				string text = display_iter.Current.GetAttribute ("text", "");
				bool enabled = Boolean.Parse (display_iter.Current.GetAttribute ("enabled", ""));

				Display d = new Display (this, index, enabled, text);
				displays.Add (d.Index, d);
			}
		}

		protected Event ParseEvent (XPathNavigator navigator, int index, ThreadGroup group)
		{
			if (navigator.Name == "Location") {
				SourceLocation location = new SourceLocation (this, navigator);
				return new SourceBreakpoint (this, index, group, location);
			} else if (navigator.Name == "Expression") {
				string expression = navigator.GetAttribute ("expression", "");
				LocationType type = (LocationType) Enum.Parse (
					typeof (LocationType), navigator.GetAttribute ("type", ""));
				return new ExpressionBreakpoint (this, index, group, type, expression);
			} else if (navigator.Name == "Exception") {
				string exc = navigator.GetAttribute ("type", "");
				bool unhandled = Boolean.Parse (navigator.GetAttribute ("unhandled", ""));
				return new ExceptionCatchPoint (index, group, exc, unhandled);
			} else if (navigator.Name == "MainMethod") {
				return new MainMethodBreakpoint (this);
			} else
				throw new InternalError ();
		}

		//
		// Modules.
		//

		public Module GetModule (string name)
		{
			return (Module) modules [name];
		}

		internal Module CreateModule (string name, ModuleGroup group)
		{
			Module module = (Module) modules [name];
			if (module != null)
				return module;

			module = new Module (group, name, null);
			modules.Add (name, module);

			return module;
		}

		bool is_user_module (string code_base)
		{
			foreach (string path in user_module_paths) {
				if (code_base.StartsWith (path))
					return true;
			}

			foreach (string user in user_modules) {
				if (user == code_base)
					return true;
			}

			return false;
		}

		internal Module CreateModule (string name, SymbolFile symfile)
		{
			if (symfile == null)
				throw new NullReferenceException ();

			Module module = (Module) modules [name];
			if (module != null)
				return module;

			ModuleGroup group;
			if (is_user_module (symfile.CodeBase))
				group = Config.GetModuleGroup ("user");
			else
				group = Config.GetModuleGroup (symfile);

			module = new Module (group, name, symfile);
			modules.Add (name, module);

			return module;
		}

		public Module[] Modules {
			get {
				Module[] retval = new Module [modules.Values.Count];
				modules.Values.CopyTo (retval, 0);
				return retval;
			}
		}

		//
		// Thread Groups
		//

		public ThreadGroup CreateThreadGroup (string name)
		{
			lock (thread_groups) {
				ThreadGroup group = (ThreadGroup) thread_groups [name];
				if (group != null)
					return group;

				group = ThreadGroup.CreateThreadGroup (name);
				thread_groups.Add (name, group);
				return group;
			}
		}

		public void DeleteThreadGroup (string name)
		{
			thread_groups.Remove (name);
		}

		public bool ThreadGroupExists (string name)
		{
			return thread_groups.Contains (name);
		}

		public ThreadGroup[] ThreadGroups {
			get {
				lock (thread_groups) {
					ThreadGroup[] retval = new ThreadGroup [thread_groups.Values.Count];
					thread_groups.Values.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		public ThreadGroup ThreadGroupByName (string name)
		{
			return (ThreadGroup) thread_groups [name];
		}

		public ThreadGroup MainThreadGroup {
			get { return main_thread_group; }
		}

		//
		// Events
		//

		public Event[] Events {
			get {
				Event[] handles = new Event [events.Count];
				events.Values.CopyTo (handles, 0);
				return handles;
			}
		}

		public Event GetEvent (int index)
		{
			return (Event) events [index];
		}

		public void AddEvent (Event handle)
		{
			events.Add (handle.Index, handle);
		}

		public void DeleteEvent (Event handle)
		{
			if (main_process != null)
				handle.Remove (main_process.MainThread);
			events.Remove (handle.Index);
		}

		//
		// Displays
		//

		public Display[] Displays {
			get {
				Display[] handles = new Display [displays.Count];
				displays.Values.CopyTo (handles, 0);
				return handles;
			}
		}

		public Display GetDisplay (int index)
		{
			return (Display) displays [index];
		}

		public Display CreateDisplay (string text)
		{
			Display d = new Display (this, text);
			displays.Add (d.Index, d);
			return d;
		}

		public void DeleteDisplay (Display d)
		{
			displays.Remove (d.Index);
		}

		//
		// File names and Directories
		//

		bool map_file_name (ref string path, string from, string to)
		{
			if (!path.StartsWith (from))
				return false;

			path = Path.Combine (to, path.Substring (from.Length + 1));
			return true;
		}

		public string MapFileName (string path)
		{
			path = DebuggerConfiguration.WindowsToUnix (path);
			foreach (KeyValuePair<string,string> map in directory_maps) {
				if (map_file_name (ref path, map.Key, map.Value))
					return path;
			}

			foreach (KeyValuePair<string,string> map in Config.DirectoryMaps) {
				if (map_file_name (ref path, map.Key, map.Value))
					return path;
			}

			return path;
		}

		//
		// User modules
		//

		public void AddUserModule (string name)
		{
			user_modules.Add (name);
		}

		public void AddUserModulePath (string path)
		{
			user_module_paths.Add (path);
		}
	}
}
