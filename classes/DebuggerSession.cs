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

		/* JIT optimization flags affecting the inferior
		 * process */
		public string JitOptimizations = null;
		public string[] JitArguments = null;

		/* The inferior process's working directory */
		public string WorkingDirectory = null;

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

		internal void GetSessionData (DataSet ds)
		{
			DataTable options_table = ds.Tables ["Options"];
			DataTable list_table = ds.Tables ["StringList"];

			int stringlist_idx = 0;

			DataRow options_row = options_table.NewRow ();
			options_row ["file"] = File;
			if ((InferiorArgs != null) && (InferiorArgs.Length > 0))
				options_row ["inferior-args"] = ++stringlist_idx;
			if ((JitArguments != null) && (JitArguments.Length > 0))
				options_row ["jit-arguments"] = ++stringlist_idx;
			if (JitOptimizations != null)
				options_row ["jit-optimizations"] = JitOptimizations;
			if (WorkingDirectory != null)
				options_row ["working-directory"] = WorkingDirectory;
			if (MonoPrefix != null)
				options_row ["mono-prefix"] = MonoPrefix;
			if (MonoPath != null)
				options_row ["mono-path"] = MonoPath;
			options_table.Rows.Add (options_row);

			if ((InferiorArgs != null) && (InferiorArgs.Length > 0)) {
				foreach (string arg in InferiorArgs) {
					DataRow row = list_table.NewRow ();
					row ["id"] = (long) options_row ["inferior-args"];
					row ["text"] = arg;
					list_table.Rows.Add (row);
				}
			}

			if ((JitArguments != null) && (JitArguments.Length > 0)) {
				foreach (string arg in JitArguments) {
					DataRow row = list_table.NewRow ();
					row ["id"] = (long) options_row ["jit-arguments"];
					row ["text"] = arg;
					list_table.Rows.Add (row);
				}
			}
		}

		private DebuggerOptions ()
		{ }

		internal DebuggerOptions (DataSet ds)
		{
			DataTable options_table = ds.Tables ["Options"];
			DataTable list_table = ds.Tables ["StringList"];

			DataRow options_row = options_table.Rows [0];

			File = (string) options_row ["file"];
			if (!options_row.IsNull ("inferior-args")) {
				long index = (long) options_row ["inferior-args"];
				DataRow[] rows = list_table.Select ("id=" + index);
				InferiorArgs = new string [rows.Length];
				for (int i = 0; i < rows.Length; i++)
					InferiorArgs [i] = (string) rows [i] ["text"];
			} else {
				InferiorArgs = new string [0];
			}
			if (!options_row.IsNull ("jit-arguments")) {
				long index = (long) options_row ["jit-arguments"];
				DataRow[] rows = list_table.Select ("id=" + index);
				JitArguments = new string [rows.Length];
				for (int i = 0; i < rows.Length; i++)
					JitArguments [i] = (string) rows [i] ["text"];
			}
			if (!options_row.IsNull ("jit-optimizations"))
				JitOptimizations = (string) options_row ["jit-optimizations"];
			if (!options_row.IsNull ("working-directory"))
				WorkingDirectory = (string) options_row ["working-directory"];
			if (!options_row.IsNull ("mono-prefix"))
				MonoPrefix = (string) options_row ["mono-prefix"];
			if (!options_row.IsNull ("mono-path"))
				MonoPath = (string) options_row ["mono-path"];
		}

		static void About ()
		{
			Console.WriteLine (
				"The Mono Debugger is (C) 2003-2006 Novell, Inc.\n\n" +
				"The debugger source code is released under the terms of the GNU GPL\n\n" +

				"For more information on Mono, visit the project Web site\n" +
				"   http://www.go-mono.com\n\n" +

				"The debugger was written by Martin Baulig and Chris Toshok");

			Environment.Exit (0);
		}

		static void Usage ()
		{
			Console.WriteLine (
				"Mono Debugger, (C) 2003-2006 Novell, Inc.\n" +
				"mdb [options] [exe-file]\n" +
				"mdb [options] -args exe-file [inferior-arguments ...]\n\n" +
				
				"   -args                     Arguments after exe-file are passed to inferior\n" +
				"   -debug-flags:PARAM        Sets the debugging flags\n" +
				"   -fullname                 Sets the debugging flags (short -f)\n" +
				"   -jit-optimizations:PARAM  Set jit optimizations used on the inferior process\n" +
				"   -mono:PATH                Override the inferior mono\n" +
				"   -mono-prefix:PATH         Override the mono prefix\n" +
				"   -native-symtabs           Load native symtabs\n" +
				"   -script                  \n" +
				"   -usage                   \n" +
				"   -version                  Display version and licensing information (short -V)\n" +
				"   -working-directory:DIR    Sets the working directory (short -cd)\n"
				);
		}

		public static DebuggerOptions ParseCommandLine (string[] args)
		{
			DebuggerOptions options = new DebuggerOptions ();
			int i;
			bool parsing_options = true;
			bool args_follow = false;

			for (i = 0; i < args.Length; i++) {
				string arg = args[i];

				if (arg == "")
					continue;

				if (!parsing_options)
					continue;

				if (arg.StartsWith ("-")) {
					if (ParseOption (options, arg, ref args, ref i, ref args_follow))
						continue;
				} else if (arg.StartsWith ("/")) {
					string unix_opt = "-" + arg.Substring (1);
					if (ParseOption (options, unix_opt, ref args, ref i, ref args_follow))
						continue;
				}

				options.File = arg;
				break;
			}

			if (args_follow) {
				string[] argv = new string [args.Length - i - 1];
				Array.Copy (args, i + 1, argv, 0, args.Length - i - 1);
				options.InferiorArgs = argv;
			} else {
				options.InferiorArgs = new string [0];
			}

			return options;
		}

		static string GetValue (ref string[] args, ref int i, string ms_val)
		{
			if (ms_val == "")
				return null;

			if (ms_val != null)
				return ms_val;

			if (i >= args.Length)
				return null;

			return args[++i];
		}

		static bool ParseDebugFlags (DebuggerOptions options, string value)
		{
			if (value == null)
				return false;

			int pos = value.IndexOf (':');
			if (pos > 0) {
				string filename = value.Substring (0, pos);
				value = value.Substring (pos + 1);
				
				options.DebugOutput = filename;
			}
			try {
				options.DebugFlags = (DebugFlags) Int32.Parse (value);
			} catch {
				return false;
			}
			options.HasDebugFlags = true;
			return true;
		}

		static bool ParseOption (DebuggerOptions debug_options,
					 string option,
					 ref string [] args,
					 ref int i,
					 ref bool args_follow_exe)
		{
			int idx = option.IndexOf (':');
			string arg, value, ms_value = null;

			if (idx == -1){
				arg = option;
			} else {
				arg = option.Substring (0, idx);
				ms_value = option.Substring (idx + 1);
			}

			switch (arg) {
			case "-args":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				args_follow_exe = true;
				return true;

			case "-working-directory":
			case "-cd":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.WorkingDirectory = value;
				return true;

			case "-debug-flags":
				value = GetValue (ref args, ref i, ms_value);
				if (!ParseDebugFlags (debug_options, value)) {
					Usage ();
					Environment.Exit (1);
				}
				return true;

			case "-jit-optimizations":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.JitOptimizations = value;
				return true;

			case "-jit-arg":
				value = GetValue (ref args, ref i, ms_value);
				if (ms_value == null) {
					Usage ();
					Environment.Exit (1);
				}
				if (debug_options.JitArguments != null) {
					string[] old = debug_options.JitArguments;
					string[] new_args = new string [old.Length + 1];
					old.CopyTo (new_args, 0);
					new_args [old.Length] = value;
					debug_options.JitArguments = new_args;
				} else {
					debug_options.JitArguments = new string[] { value };
				}
				return true;

			case "-fullname":
			case "-f":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.InEmacs = true;
				return true;

			case "-mono-prefix":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.MonoPrefix = value;
				return true;

			case "-mono":
				value = GetValue (ref args, ref i, ms_value);
				if (value == null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.MonoPath = value;
				return true;

			case "-script":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.IsScript = true;
				return true;

			case "-version":
			case "-V":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				About();
				Environment.Exit (1);
				return true;

			case "-help":
			case "--help":
			case "-h":
			case "-usage":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				Usage();
				Environment.Exit (1);
				return true;

			case "-run":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.StartTarget = true;
				return true;
			}

			return false;
		}
	}

	public delegate void ModulesChangedHandler (DebuggerSession session);

	[Serializable]
	public class DebuggerSession : DebuggerMarshalByRefObject
	{
		public readonly DebuggerConfiguration Config;
		public readonly DebuggerOptions Options;

		DataSet saved_session;
		SessionData data;

		private DebuggerSession (DebuggerConfiguration config)
		{
			this.Config = config;
		}

		public DebuggerSession (DebuggerConfiguration config, DebuggerOptions options)
			: this (config)
		{
			this.Options = options;
		}

		public DebuggerSession (DebuggerConfiguration config, Stream stream)
			: this (config)
		{
			saved_session = new DataSet ("DebuggerSession");

			Assembly ass = Assembly.GetExecutingAssembly ();
			using (Stream schema = ass.GetManifestResourceStream ("DebuggerSession"))
				saved_session.ReadXmlSchema (schema);

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
			return new DebuggerSession (Config, Options);
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
				DataTable module_table = ds.Tables ["Module"];
				foreach (DataRow row in module_table.Rows) {
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

				Hashtable locations = new Hashtable ();
				DataTable location_table = ds.Tables ["Location"];
				foreach (DataRow row in location_table.Rows) {
					long index = (long) row ["id"];
					locations.Add (index, new SourceLocation (Session, row));
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

					long loc_index = (long) row ["location"];
					SourceLocation location = (SourceLocation) locations [loc_index];
					int index = (int) (long) row ["index"];
					Breakpoint bpt = new Breakpoint (index, group, location);
					AddEvent (bpt);
					bpt.Enable (process.MainThread);
				}
			}

			public DataSet SaveSession ()
			{
				DataSet ds = new DataSet ("DebuggerSession");

				Assembly ass = Assembly.GetExecutingAssembly ();
				using (Stream schema = ass.GetManifestResourceStream ("DebuggerSession"))
					ds.ReadXmlSchema (schema);

				Session.Options.GetSessionData (ds);

				DataTable group_table = ds.Tables ["ModuleGroup"];
				foreach (ModuleGroup group in Session.Config.ModuleGroups) {
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
					if (!(e is Breakpoint))
					    continue;
					DataRow row = event_table.NewRow ();
					e.GetSessionData (row);
					event_table.Rows.Add (row);
				}

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
