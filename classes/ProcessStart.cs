using System;
using System.IO;
using System.Configuration;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger
{
	public class DebuggerOptions
	{
		/* The executable file we're debugging */
		public string File = "";

		/* argv[1...n] for the inferior process */
		public string[] InferiorArgs = null;

		/* The command line prompt.  should we really even
		 * bother letting the user set this?  why? */
		public string Prompt = "(mdb) ";

		/* JIT optimization flags affecting the inferior
		 * process */
		public string JitOptimizations = "";

		/* The inferior process's working directory */
		public string WorkingDirectory = Environment.CurrentDirectory;

		/* Whether or not we load native symbol tables */
		public bool LoadNativeSymbolTable = false;

		/* true if we're running in a script */
		public bool IsScript = false;

		/* true if we want to start the application immediately */
		public bool StartTarget = false;
	  
		/* the value of the -debug-flags: command line
		 * argument */
		public int DebugFlags = 0;

		/* true if -f/-fullname is specified on the command
		 * line */
		public bool InEmacs = false;

		/* non-null if the user specified the -mono-prefix
		 * command line argument */
		public string MonoPrefix = null;
	}

	[Serializable]
	public class ProcessStart
	{
		public string WorkingDirectory;
		public string[] UserArguments;
		public string[] UserEnvironment;
		public bool IsNative;
		public bool LoadNativeSymbolTable;

		string cwd;
		string base_dir;
		string[] argv;
		string[] envp;
		bool native;
		[NonSerialized] Assembly application;
		[NonSerialized] DebuggerOptions options;

		public static string JitWrapper;
		bool initialized = false;

		static ProcessStart ()
		{
			string libdir = AssemblyInfo.libdir;
			JitWrapper = Path.Combine (libdir, "mono-debugger-mini-wrapper");
		}

		public ProcessStart ()
		{ }

		public ProcessStart (DebuggerOptions the_options, string[] argv)
		{
			if (the_options == null)
				options = new DebuggerOptions ();
			else
				options = the_options;

			if ((argv == null) || (argv.Length == 0))
				throw new ArgumentException ();

			this.UserArguments = argv;
			this.WorkingDirectory = options.WorkingDirectory;

			try {
				application = Assembly.LoadFrom (argv [0]);
			} catch {
				application = null;
			}

			if (application != null) {
				string error = C.MonoDebuggerSupport.CheckRuntimeVersion (argv [0]);
				if (error != null)
					throw new TargetException (
						TargetError.CannotStartTarget, "Cannot start target: {0}",
						error);

				LoadNativeSymbolTable = Options.LoadNativeSymbolTable;
				IsNative = false;

				string[] start_argv = {
					JitWrapper, options.JitOptimizations
				};

				this.argv = new string [argv.Length + start_argv.Length];
				start_argv.CopyTo (this.argv, 0);
				argv.CopyTo (this.argv, start_argv.Length);
			} else {
				LoadNativeSymbolTable = true;
				IsNative = true;

				this.argv = argv;
			}

			DoSetup ();
		}

		public DebuggerOptions Options {
			get { return options; }
		}

		public string[] CommandLineArguments {
			get { return argv; }
		}

		public string[] Environment {
			get { return envp; }
		}

		public string TargetApplication {
			get { return argv [0]; }
		}

		public string BaseDirectory {
			get { return base_dir; }
		}

		void AddUserEnvironment (Hashtable hash)
		{
			if (UserEnvironment == null)
				return;
			foreach (string line in UserEnvironment) {
				int pos = line.IndexOf ('=');
				if (pos < 0)
					throw new ArgumentException ();

				string name = line.Substring (0, pos);
				string value = line.Substring (pos + 1);

				hash.Add (name, value);
			}
		}

		void add_env_path (Hashtable hash, string name, string value)
		{
			if (!hash.Contains (name))
				hash.Add (name, value);
			else {
				string old = (string) hash [name];
				hash [name] = value + ":" + value;
			}
		}

		protected virtual void SetupEnvironment ()
		{
			Hashtable hash = new Hashtable ();
			if (UserEnvironment != null)
				AddUserEnvironment (hash);

			IDictionary env_vars = System.Environment.GetEnvironmentVariables ();
			foreach (string var in env_vars.Keys) {
				if (var == "GC_DONT_GC")
					continue;

				// Allow `UserEnvironment' to override env vars.
				if (hash.Contains (var))
					continue;
				hash.Add (var, env_vars [var]);
			}

			if (Options.MonoPrefix != null) {
				string prefix = Options.MonoPrefix;
				add_env_path (hash, "MONO_GAC_PREFIX", prefix);
				add_env_path (hash, "MONO_PATH", prefix + "/lib");
				add_env_path (hash, "LD_LIBRARY_PATH", prefix + "/lib");
				add_env_path (hash, "PATH", prefix + "/bin");
			}

			ArrayList list = new ArrayList ();
			foreach (DictionaryEntry entry in hash) {
				string key = (string) entry.Key;
				string value = (string) entry.Value;

				list.Add (key + "=" + value);
			}

			envp = new string [list.Count];
			list.CopyTo (envp, 0);
		}

		protected virtual void SetupWorkingDirectory ()
		{
			cwd = (WorkingDirectory != null) ? WorkingDirectory : ".";
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
			SetupEnvironment ();
			SetupArguments ();
			SetupWorkingDirectory ();
			SetupBaseDirectory ();
			initialized = true;
		}

		public static ProcessStart Create (DebuggerOptions options)
		{
			if (options.File == null || options.File == "")
				return null;

			string[] args = new string[1 + (options.InferiorArgs == null ? 0 : options.InferiorArgs.Length)];

			args[0] = options.File;
			options.InferiorArgs.CopyTo (args, 1);

			return new ProcessStart (options, args);
		}

		public static ProcessStart Create (DebuggerOptions options, string[] argv)
		{
			if (argv.Length == 0)
				return null;

			return new ProcessStart (options, argv);
		}

		protected string print_argv (string[] argv)
		{
			if (argv == null)
				return "null";
			else
				return String.Join (":", argv);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2},{3},{4},{5})",
					      GetType (), WorkingDirectory,
					      print_argv (UserArguments),
					      print_argv (UserEnvironment),
					      IsNative, LoadNativeSymbolTable);
		}
	}
}
