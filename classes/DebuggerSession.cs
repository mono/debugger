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
	public delegate void ModulesChangedHandler (DebuggerSession session);

	[Serializable]
	public class DebuggerSession : DebuggerMarshalByRefObject
	{
		public readonly string Name;
		public readonly DebuggerConfiguration Config;
		public readonly DebuggerOptions Options;

		DataSet saved_session;
		SessionData data;

		private DebuggerSession (DebuggerConfiguration config, string name)
		{
			this.Config = config;
			this.Name = name;
		}

		public DebuggerSession (DebuggerConfiguration config, DebuggerOptions options, string name)
			: this (config, name)
		{
			this.Options = options;
		}

		public DebuggerSession (DebuggerConfiguration config, Stream stream)
			: this (config, "main")
		{
			saved_session = DebuggerConfiguration.CreateDataSet ();
			saved_session.ReadXml (stream, XmlReadMode.IgnoreSchema);

			Options = new DebuggerOptions (saved_session);
		}

		protected SessionData Data {
			get {
				if (data == null)
					throw new TargetException (TargetError.NoTarget);

				return data;
			}
		}

		internal DebuggerSession Clone ()
		{
			return new DebuggerSession (Config, Options, Name);
		}

		//
		// Modules.
		//

		public Module GetModule (string name)
		{
			return Data.GetModule (name);
		}

		internal Module CreateModule (string name, ModuleGroup group)
		{
			return Data.CreateModule (name, group);
		}

		internal Module CreateModule (string name, SymbolFile symfile)
		{
			return Data.CreateModule (name, symfile);
		}

		public Module[] Modules {
			get { return Data.Modules; }
		}

		//
		// Thread Groups
		//

		public ThreadGroup CreateThreadGroup (string name)
		{
			return Data.CreateThreadGroup (name);
		}

		public void DeleteThreadGroup (string name)
		{
			Data.DeleteThreadGroup (name);
		}

		public bool ThreadGroupExists (string name)
		{
			return Data.ThreadGroupExists (name);
		}

		public ThreadGroup[] ThreadGroups {
			get { return Data.ThreadGroups; }
		}

		public ThreadGroup ThreadGroupByName (string name)
		{
			return Data.ThreadGroupByName (name);
		}

		public ThreadGroup MainThreadGroup {
			get { return Data.MainThreadGroup; }
		}

		//
		// Events
		//

		public Event[] Events {
			get { return Data.Events; }
		}

		public Event GetEvent (int index)
		{
			return Data.GetEvent (index);
		}

		internal void AddEvent (Event handle)
		{
			Data.AddEvent (handle);
		}

		public void DeleteEvent (Thread thread, Event handle)
		{
			Data.DeleteEvent (thread, handle);
		}

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

		//
		// FIXME: Ideally, this would be called automatically from the
		//        SingleSteppingEngine.  The problem is that `Event.Enable()' may
		//        need to run stepping operations, so doing this correctly would
		//        require some more work.  Keeping this as a quick fix for the
		//        moment.
		//
		public void MainProcessReachedMain (Process process)
		{
			if (saved_session == null)
				return;

			data.LoadSession (process, saved_session);
		}

		internal void OnProcessCreated (Process process)
		{
			data = new SessionData (this);
		}

		internal void OnProcessExited (Process process)
		{
			try {
				saved_session = Data.SaveSession ();
			} finally {
				data = null;
			}
		}

		public void SaveSession (Stream stream)
		{
			saved_session = Data.SaveSession ();
			saved_session.WriteXml (stream);
		}

		protected class SessionData
		{
			public readonly DebuggerSession Session;

			private readonly Hashtable modules;
			private readonly Hashtable events;
			private readonly Hashtable thread_groups;
			private readonly ThreadGroup main_thread_group;

			public SessionData (DebuggerSession session)
			{
				this.Session = session;

				modules = Hashtable.Synchronized (new Hashtable ());
				events = Hashtable.Synchronized (new Hashtable ());
				thread_groups = Hashtable.Synchronized (new Hashtable ());
				main_thread_group = CreateThreadGroup ("main");
			}

			public void LoadSession (Process process, DataSet ds)
			{
				string query = String.Format ("name='{0}'", Session.Name);
				DataRow session_row = ds.Tables ["DebuggerSession"].Select (query) [0];

				DataRow[] group_rows = session_row.GetChildRows ("Session_ModuleGroup");
				foreach (DataRow row in group_rows) {
					string name = (string) row ["name"];
					ModuleGroup group = Session.Config.CreateModuleGroup (name);
					group.SetSessionData (row);
				}

				DataRow[] module_rows = session_row.GetChildRows ("Session_Module");
				foreach (DataRow row in module_rows) {
					string name = (string) row ["name"];
					Module module = (Module) modules [name];
					if (module == null) {
						string gname = (string) row ["group"];
						ModuleGroup group = Session.Config.GetModuleGroup (gname);
						module = new Module (group, name, null);
						modules.Add (name, module);
					}

					module.SetSessionData (row);
				}

				DataRow[] event_rows = session_row.GetChildRows ("Session_Event");
				foreach (DataRow row in event_rows) {
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

					int index = (int) (long) row ["index"];
					DataRow[] location_rows = row.GetChildRows ("Location_Event");
					SourceLocation location = new SourceLocation (Session, location_rows [0]);

					Breakpoint bpt = new Breakpoint (index, group, location);
					AddEvent (bpt);
					bpt.Enable (process.MainThread);
				}
			}

			public DataSet SaveSession ()
			{
				DataSet ds = DebuggerConfiguration.CreateDataSet ();

				{
					DataTable session_table = ds.Tables ["DebuggerSession"];
					DataRow row = session_table.NewRow ();
					row ["name"] = Session.Name;
					session_table.Rows.Add (row);
				}

				Session.Options.GetSessionData (ds, Session);

				DataTable group_table = ds.Tables ["ModuleGroup"];
				foreach (ModuleGroup group in Session.Config.ModuleGroups) {
					DataRow row = group_table.NewRow ();
					row ["session"] = Session.Name;
					group.GetSessionData (row);
					group_table.Rows.Add (row);
				}

				DataTable module_table = ds.Tables ["Module"];
				foreach (Module module in Modules) {
					DataRow row = module_table.NewRow ();
					row ["session"] = Session.Name;
					module.GetSessionData (row);
					module_table.Rows.Add (row);
				}

				DataTable thread_group_table = ds.Tables ["ThreadGroup"];
				foreach (ThreadGroup group in ThreadGroups) {
					DataRow row = thread_group_table.NewRow ();
					row ["session"] = Session.Name;
					row ["name"] = group.Name;
					thread_group_table.Rows.Add (row);
				}

				foreach (Event e in Events)
					e.GetSessionData (ds, Session);

				return ds;
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

				ModuleGroup group = Session.Config.GetModuleGroup (symfile);

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
		}
	}
}
