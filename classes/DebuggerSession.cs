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
using System.Data;
using System.Data.Common;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	[Serializable]
	public class DebuggerOptions
	{
		/* The executable file we're debugging */
		public string File = null;

		/* argv[1...n] for the inferior process */
		public string[] InferiorArgs = null;

		/* The command line prompt.  should we really even
		 * bother letting the user set this?  why? */
		public string Prompt = "(mdb) ";

		/* JIT optimization flags affecting the inferior
		 * process */
		public string JitOptimizations = "";

		public string[] JitArguments = null;

		/* The inferior process's working directory */
		public string WorkingDirectory = Environment.CurrentDirectory;

		/* Whether or not we load native symbol tables */
		public bool LoadNativeSymbolTable = true;

		/* true if we're running in a script */
		public bool IsScript = false;

		/* true if we want to start the application immediately */
		public bool StartTarget = false;
	  
		/* the value of the -debug-flags: command line argument */
		public bool HasDebugFlags = false;
		public DebugFlags DebugFlags = DebugFlags.None;
		public string DebugOutput = null;

		/* true if -f/-fullname is specified on the command line */
		public bool InEmacs = false;

		/* non-null if the user specified the -mono-prefix
		 * command line argument */
		public string MonoPrefix = null;

		/* non-null if the user specified the -mono command line argument */
		public string MonoPath = null;

		Hashtable user_environment;

		public Hashtable UserEnvironment {
			get { return user_environment; }
		}

		public void SetEnvironment (string name, string value)
		{
			if (user_environment == null)
				user_environment = new Hashtable ();

			if (user_environment.Contains (name)) {
				if (value == null)
					user_environment.Remove (name);
				else
					user_environment [name] = value;
			} else if (value != null)
				user_environment.Add (name, value);
		}
	}

	public delegate void ModulesChangedHandler (DebuggerSession session);

	[Serializable]
	public class DebuggerSession : DebuggerMarshalByRefObject
	{
		public readonly DebuggerConfiguration Config;
		public readonly DebuggerOptions Options;

		private readonly Hashtable modules;
		private readonly Hashtable events;
		private readonly Hashtable thread_groups;
		private readonly ThreadGroup main_thread_group;

		private DebuggerSession (DebuggerConfiguration config)
		{
			this.Config = config;

			modules = Hashtable.Synchronized (new Hashtable ());
			events = Hashtable.Synchronized (new Hashtable ());
			thread_groups = Hashtable.Synchronized (new Hashtable ());
			main_thread_group = CreateThreadGroup ("main");
		}

		public DebuggerSession (DebuggerConfiguration config, DebuggerOptions options)
			: this (config)
		{
			this.Options = options;
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

		public void DeleteEvent (Thread thread, Event handle)
		{
			handle.Remove (thread);
			events.Remove (handle.Index);
		}

		//
		// Events
		//

		public Event InsertBreakpoint (Thread target, ThreadGroup group, int domain,
					       SourceLocation location)
		{
			if (!location.HasMethod && !location.HasFunction)
				throw new TargetException (TargetError.LocationInvalid);

			Event handle = new Breakpoint (group, location);
			AddEvent (handle);
			return handle;
		}

		public Event InsertBreakpoint (Thread target, ThreadGroup group,
					       TargetAddress address)
		{
			Event handle = new Breakpoint (address.ToString (), group, address);
			AddEvent (handle);
			return handle;
		}

		public Event InsertBreakpoint (Thread target, ThreadGroup group,
					       TargetFunctionType func)
		{
			Event handle = new Breakpoint (group, new SourceLocation (func));
			AddEvent (handle);
			return handle;
		}

		public Event InsertExceptionCatchPoint (Thread target, ThreadGroup group,
							TargetType exception)
		{
			Event handle = new ExceptionCatchPoint (group, exception);
			AddEvent (handle);
			return handle;
		}

		public Event InsertHardwareWatchPoint (Thread target, TargetAddress address,
						       BreakpointType type)
		{
			Event handle = new Breakpoint (address, type);
			AddEvent (handle);
			return handle;
		}

		//
		// Session management.
		//

		public void SaveSession (Stream stream)
		{
			DataSet ds = new DataSet ("DebuggerSession");

			Assembly ass = Assembly.GetExecutingAssembly ();
			using (Stream schema = ass.GetManifestResourceStream ("DebuggerSession"))
				ds.ReadXmlSchema (schema);

			DataTable group_table = ds.Tables ["ModuleGroup"];
			foreach (ModuleGroup group in Config.ModuleGroups) {
				DataRow row = group_table.NewRow ();
				group.GetSessionData (row);
				group_table.Rows.Add (row);
			}

			DataTable module_table = ds.Tables ["Module"];
			foreach (Module module in Modules) {
				DataRow row = module_table.NewRow ();
				module.GetSessionData (row);
				module_table.Rows.Add (row);
			}

			DataTable thread_group_table = ds.Tables ["ThreadGroup"];
			foreach (ThreadGroup group in ThreadGroups) {
				DataRow row = thread_group_table.NewRow ();
				row ["name"] = group.Name;
				thread_group_table.Rows.Add (row);
			}

			DataTable event_table = ds.Tables ["Event"];
			foreach (Event e in Events) {
				DataRow row = event_table.NewRow ();
				e.GetSessionData (row);
				event_table.Rows.Add (row);
			}

			ds.WriteXml (stream);
		}

		public DebuggerSession (DebuggerConfiguration config, DebuggerOptions options,
					Stream stream)
			: this (config)
		{
			this.Options = options;

			DataSet ds = new DataSet ("DebuggerSession");

			Assembly ass = Assembly.GetExecutingAssembly ();
			using (Stream schema = ass.GetManifestResourceStream ("DebuggerSession"))
				ds.ReadXmlSchema (schema);

			XmlDataDocument doc = new XmlDataDocument (ds);

			ds.ReadXml (stream, XmlReadMode.IgnoreSchema);

			DataTable module_table = ds.Tables ["Module"];
			foreach (DataRow row in module_table.Rows) {
				string name = (string) row ["name"];
				Module module = (Module) modules [name];
				if (module == null) {
					string gname = (string) row ["group"];
					ModuleGroup group = Config.GetModuleGroup (gname);
					module = new Module (group, name, null);
					modules.Add (name, module);
				}

				module.SetSessionData (row);
			}

			Hashtable locations = new Hashtable ();
			DataTable location_table = ds.Tables ["Location"];
			foreach (DataRow row in location_table.Rows) {
				long index = (long) row ["id"];
				locations.Add (index, new SourceLocation (this, row));
			}

			DataTable event_table = ds.Tables ["Event"];
			foreach (DataRow row in event_table.Rows) {
				if ((string) row ["type"] != "Mono.Debugger.Breakpoint")
					continue;

				string gname = (string) row ["group"];
				ThreadGroup group;
				if (gname == "system")
					group = ThreadGroup.System;
				else if (gname == "global")
					group = ThreadGroup.Global;
				else
					group = CreateThreadGroup (gname);

				SourceLocation location = (SourceLocation) locations [(long) row ["location"]];
				Breakpoint bpt = new Breakpoint (group, location);
				AddEvent (bpt);
			}
		}

		//
		// Session management.
		//

		internal void OnProcessReachedMain (Process process)
		{
			foreach (Event e in events.Values) {
				e.Enable (process.MainThread);
			}
		}

		internal void OnProcessExited (Process process)
		{
			foreach (Event e in events.Values) {
				e.OnTargetExited ();
			}
		}
	}
}
