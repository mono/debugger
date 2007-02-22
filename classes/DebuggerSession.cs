using System;
using System.IO;
using System.Reflection;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml;
using System.Xml.XPath;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public delegate void ModulesChangedHandler (DebuggerSession session);

	[Serializable]
	public class DebuggerSession : DebuggerMarshalByRefObject
	{
		public readonly string Name;
		public readonly DebuggerConfiguration Config;
		public readonly DebuggerOptions Options;

		protected readonly Hashtable modules;
		protected readonly Hashtable events;
		protected readonly Hashtable thread_groups;
		protected readonly ThreadGroup main_thread_group;

		Process main_process;
		XmlDocument saved_session;

		private DebuggerSession (DebuggerConfiguration config, string name)
		{
			this.Config = config;
			this.Name = name;

			modules = Hashtable.Synchronized (new Hashtable ());
			events = Hashtable.Synchronized (new Hashtable ());
			thread_groups = Hashtable.Synchronized (new Hashtable ());
			main_thread_group = CreateThreadGroup ("main");
		}

		public DebuggerSession (DebuggerConfiguration config, DebuggerOptions options, string name)
			: this (config, name)
		{
			this.Options = options;
		}

		internal DebuggerSession Clone (DebuggerOptions new_options, string new_name)
		{
			return new DebuggerSession (Config, new_options, new_name);
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

			return doc;
		}

		public DebuggerSession (DebuggerConfiguration config, Stream stream)
			: this (config, "main")
		{
			XmlValidatingReader reader = new XmlValidatingReader (new XmlTextReader (stream));
			Assembly ass = Assembly.GetExecutingAssembly ();
			using (Stream schema = ass.GetManifestResourceStream ("DebuggerConfiguration"))
				reader.Schemas.Add (null, new XmlTextReader (schema));

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

			LoadSession (nav);
		}

		void AddThreadGroup (XmlElement root, ThreadGroup group)
		{
			XmlElement element = root.OwnerDocument.CreateElement ("ThreadGroup");
			element.SetAttribute ("name", group.Name);

			root.AppendChild (element);
		}

		public Event InsertBreakpoint (Thread target, ThreadGroup group,
					       SourceLocation location)
		{
			Event handle = new SourceBreakpoint (this, group, location);
			handle.Enable (target);
			AddEvent (handle);
			return handle;
		}

		public Event InsertBreakpoint (Thread target, ThreadGroup group,
					       TargetAddress address)
		{
			Event handle = new AddressBreakpoint (address.ToString (), group, address);
			handle.Enable (target);
			AddEvent (handle);
			return handle;
		}

		public Event InsertExceptionCatchPoint (Thread target, ThreadGroup group,
							TargetType exception)
		{
			Event handle = new ExceptionCatchPoint (group, exception);
			handle.Enable (target);
			AddEvent (handle);
			return handle;
		}

		public Event InsertHardwareWatchPoint (Thread target, TargetAddress address,
						       HardwareWatchType type)
		{
			Event handle = new AddressBreakpoint (type, address);
			handle.Enable (target);
			AddEvent (handle);
			return handle;
		}

		//
		// Session management.
		//

		//
		// FIXME: Ideally, this would be called automatically from the
		//        SingleSteppingEngine.  The problem is that `Event.Enable()' may
		//        need to run stepping operations, so doing this correctly would
		//        require some more work.  Keeping this as a quick fix for the
		//        moment.
		//
		public void MainProcessReachedMain (Process process)
		{
			main_process = process;

			foreach (Event e in events.Values) {
				e.Enable (process.MainThread);
			}
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

				SourceLocation location = null;

				XPathNodeIterator children = event_iter.Current.SelectChildren (
					XPathNodeType.Element);
				while (children.MoveNext ()) {
					if (children.Current.Name == "Location")
						location = new SourceLocation (this, children.Current);
					else
						throw new InternalError ();
				}

				Breakpoint bpt = new SourceBreakpoint (this, index, group, location);
				bpt.IsEnabled = enabled;
				AddEvent (bpt);
			}
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

		internal Module CreateModule (string name, SymbolFile symfile)
		{
			if (symfile == null)
				throw new NullReferenceException ();

			Module module = (Module) modules [name];
			if (module != null)
				return module;

			ModuleGroup group = Config.GetModuleGroup (symfile);

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

		internal void AddEvent (Event handle)
		{
			events.Add (handle.Index, handle);
		}

		public void DeleteEvent (Event handle)
		{
			if (main_process != null)
				handle.Remove (main_process.MainThread);
			events.Remove (handle.Index);
		}
	}
}
