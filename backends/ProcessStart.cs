using System;
using System.IO;
using System.Configuration;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Mono.GetOptions;

namespace Mono.Debugger
{
	public class DebuggerOptions : Options
	{
		public enum StartMode
		{
			Unknown,
			CoreFile,
			LoadSession,
			StartApplication
		}

		public DebuggerOptions ()
		{
			ParsingMode = OptionsParsingMode.Linux;
			EndOptionProcessingWithDoubleDash = true;
		}

		StartMode start_mode = StartMode.Unknown;

		public StartMode Mode {
			get { return start_mode; }
		}

		[Option("PARAM is one of `core' (to load a core file),\n\t\t\t" +
			"   `load' (to load a previously saved debugging session)\n\t\t\t" +
			"   or `start' (to start a new application).", 'm')]
		public WhatToDoNext mode (string value)
		{
			if (start_mode != StartMode.Unknown) {
				Console.WriteLine ("This argument cannot be used multiple times.");
				return WhatToDoNext.AbandonProgram;
			}

			switch (value) {
			case "core":
				start_mode = StartMode.CoreFile;
				return WhatToDoNext.GoAhead;

			case "load":
				start_mode = StartMode.LoadSession;
				return WhatToDoNext.GoAhead;

			case "start":
				start_mode = StartMode.StartApplication;
				return WhatToDoNext.GoAhead;

			default:
				Console.WriteLine ("Invalid `--mode' argument.");
				return WhatToDoNext.AbandonProgram;
			}
		}

		[Option("The command-line prompt", 'p', "prompt")]
		public string Prompt = "$";

		[Option("Full path name of the JIT wrapper", "jit-wrapper")]
		public string JitWrapper = ProcessStart.Path_Mono;

		[Option("JIT Optimizations", "jit-optimizations")]
		public string JitOptimizations = "";

		[Option("Working directory", "working-directory")]
		public string WorkingDirectory = ".";

		[Option("Display version and licensing information", 'V', "version")]
		public override WhatToDoNext DoAbout()
		{
			base.DoAbout ();
			return WhatToDoNext.AbandonProgram;
		}
	}

	[Serializable]
	public abstract class ProcessStart
	{
		public static string Path_Mono			= "mono-debugger-mini-wrapper";
		public static string Environment_Path		= "/usr/bin";
		public static string Environment_LibPath	= "";

		string cwd;
		protected string base_dir;
		string[] argv;
		string[] envp;
		string core_file;
		DebuggerOptions options;
		protected bool native;
		protected bool load_native_symtab;

		bool initialized = false;

		static ProcessStart ()
		{
			NameValueCollection settings = ConfigurationSettings.AppSettings;

			foreach (string key in settings.AllKeys) {
				string value = settings [key];

				switch (key) {
				case "mono-path":
					Path_Mono = value;
					break;

				case "environment-path":
					Environment_Path = value;
					break;

				case "environment-libpath":
					Environment_LibPath = value;
					break;
				}
			}
		}

		public ProcessStart (DebuggerOptions options, string[] argv, string core_file)
			: this (options)
		{
			this.argv = argv;
			this.core_file = core_file;
		}

		public ProcessStart (DebuggerOptions options)
		{
			this.options = options;
			this.cwd = options.WorkingDirectory;
		}

		public string WorkingDirectory {
			get {
				return cwd;
			}
		}

		public string BaseDirectory {
			get {
				if (!initialized)
					DoSetup ();

				return base_dir;
			}
		}

		public string[] CommandLineArguments {
			get {
				if (!initialized)
					DoSetup ();

				return argv;
			}
		}

		public string[] Environment {
			get {
				if (!initialized)
					DoSetup ();

				return envp;
			}
		}

		public string TargetApplication {
			get {
				if (!initialized)
					DoSetup ();

				return argv [0];
			}
		}

		public bool IsNative {
			get {
				if (!initialized)
					DoSetup ();

				return native;
			}
		}

		public bool LoadNativeSymtab {
			get {
				if (!initialized)
					DoSetup ();

				return load_native_symtab;
			}
		}

		public bool IsCoreFile {
			get { return core_file != null; }
		}

		public string CoreFile {
			get { return core_file; }
		}

		public virtual string CommandLine {
			get { return String.Join (" ", argv); }
		}

		protected virtual void SetupEnvironment (params string[] add_envp)
		{
			ArrayList list = new ArrayList ();
			if (envp != null)
				list.AddRange (envp);
			if (add_envp != null)
				list.AddRange (add_envp);

			IDictionary env_vars = System.Environment.GetEnvironmentVariables ();

                        foreach (string name in env_vars.Keys) {
				if ((name == "PATH") || (name == "LD_LIBRARY_PATH") ||
				    (name == "LD_BIND_NOW"))
					continue;

				list.Add (name + "=" + env_vars [name]);
			}

			envp = new string [list.Count];
			list.CopyTo (envp, 0);
		}

		protected virtual void SetupWorkingDirectory ()
		{
			if (cwd == null)
				cwd = ".";
		}

		protected virtual void SetupBaseDirectory ()
		{
			if (base_dir == null)
				base_dir = GetFullPath (Path.GetDirectoryName (argv [0]));
		}

		protected string GetFullPath (string path)
		{
			string full_path;
			if (path.StartsWith ("./"))
				full_path = Path.GetFullPath (path.Substring (2));
			else if (path.Length > 0)
				full_path = Path.GetFullPath (path);
			else // FIXME: should search $PATH or something too
				full_path = Path.GetFullPath ("./");

			if (full_path.EndsWith ("/."))
				full_path = full_path.Substring (0, full_path.Length-2);
			return full_path;
		}

		protected virtual string[] SetupArguments ()
		{
			if ((argv == null) || (argv.Length < 1))
				throw new Exception ("Invalid command line arguments");
			return argv;
		}

		protected virtual void DoSetup ()
		{
			SetupEnvironment ("PATH=" + Environment_Path, "LD_BIND_NOW=yes",
					  "LD_LIBRARY_PATH=" + Environment_LibPath);
			argv = SetupArguments ();
			SetupWorkingDirectory ();
			SetupBaseDirectory ();
			load_native_symtab = true;
			native = true;
			initialized = true;
		}

		protected static ProcessStart Create (DebuggerOptions options, string[] argv, string core_file)
		{
			Assembly application;
			try {
				application = Assembly.LoadFrom (argv [0]);
			} catch (Exception e) {
				application = null;
			}

			if (application != null)
				return new ManagedProcessStart (options, argv, application, core_file);
			else
				return new NativeProcessStart (options, argv, core_file);
		}

		public static ProcessStart Create (DebuggerOptions options, string[] args)
		{
			if (options == null)
				options = new DebuggerOptions ();

			options.ProcessArgs (args);
			args = options.RemainingArguments;

			switch (options.Mode) {
			case DebuggerOptions.StartMode.CoreFile:
				if (args.Length < 2)
					throw new CannotStartTargetException (
						"You need to specify at least the name of " +
						"the core file and the application it was " +
						"generated from.");

				string core_file = args [0];
				string [] temp_args = new string [args.Length-1];
				Array.Copy (args, 1, temp_args, 0, args.Length-1);
				args = temp_args;

				return Create (options, args, core_file);

			case DebuggerOptions.StartMode.LoadSession:
				if (args.Length != 1)
					throw new CannotStartTargetException (
						"This mode requires exactly one argument, " +
						"the file to load the session from.");

				return new SessionProcessStart (options, args [0]);

			case DebuggerOptions.StartMode.Unknown:
				if (args.Length == 0)
					return null;

				return Create (options, args, null);

			default:
				if (args.Length == 0)
					throw new CannotStartTargetException (
						"You need to specify the program you want " +
						"to debug.");

				return Create (options, args, null);
			}
		}

		public abstract DebuggerBackend Run ();
	}

	[Serializable]
	public class SessionProcessStart : ProcessStart
	{
		string filename;

		public SessionProcessStart (DebuggerOptions options, string filename)
			: base (options)
		{
			this.filename = filename;
		}

		public override DebuggerBackend Run ()
		{
			StreamingContext context = new StreamingContext (StreamingContextStates.All, this);
			BinaryFormatter formatter = new BinaryFormatter (null, context);

			using (FileStream stream = new FileStream (filename, FileMode.Open)) {
				return (DebuggerBackend) formatter.Deserialize (stream);
			}
		}
	}

	[Serializable]
	public class NativeProcessStart : ProcessStart
	{
		public NativeProcessStart (DebuggerOptions options, string[] argv, string core_file)
			: base (options, argv, core_file)
		{
		}

		public override DebuggerBackend Run ()
		{
			return new DebuggerBackend ();
		}
	}

	[Serializable]
	public class ManagedProcessStart : NativeProcessStart
	{
		Assembly application;
		string[] old_argv;
		string jit_wrapper, opt_flags;

		public ManagedProcessStart (DebuggerOptions options, string[] argv, Assembly application,
					    string core_file)
			: base (options, argv, core_file)
		{
			this.application = application;
			this.jit_wrapper = options.JitWrapper;
			this.opt_flags = options.JitOptimizations;
		}

		protected override void DoSetup ()
		{
			base.DoSetup ();
			load_native_symtab = false;
			native = false;
		}

		protected override void SetupBaseDirectory ()
		{
			if (base_dir == null)
				base_dir = GetFullPath (Path.GetDirectoryName (old_argv [0]));
		}

		public override string CommandLine {
			get { return String.Join (" ", old_argv); }
		}

		protected override string[] SetupArguments ()
		{
			old_argv = base.SetupArguments ();

			MethodInfo main = application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string[] start_argv = { jit_wrapper, opt_flags };

			string[] new_argv = new string [old_argv.Length + start_argv.Length];
			start_argv.CopyTo (new_argv, 0);
			old_argv.CopyTo (new_argv, start_argv.Length);
			return new_argv;
		}
	}
}
