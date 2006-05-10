using System;
using System.IO;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;

namespace Mono.Debugger.Backends
{
	internal sealed class ProcessStart : DebuggerMarshalByRefObject
	{
		public readonly int PID;
		public readonly string CoreFile;
		public readonly string[] UserEnvironment;
		public readonly bool IsNative;
		public readonly bool LoadNativeSymbolTable = true;

		string cwd;
		string base_dir;
		string[] argv;
		string[] envp;
		DebuggerOptions options;

		public static string MonoPath;

		static ProcessStart ()
		{
			/* Use relative path based on where mscorlib.dll is at to enable relocation */
			string prefix = new DirectoryInfo (Path.GetDirectoryName (typeof (int).Assembly.Location)).Parent.Parent.Parent.FullName;
			MonoPath = Path.Combine (Path.Combine (prefix, "bin"), "mono");
		}

		protected static bool IsMonoAssembly (string filename)
		{
			try {
				using (FileStream stream = new FileStream (filename, FileMode.Open, FileAccess.Read)) {
					byte[] data = new byte [128];
					if (stream.Read (data, 0, 128) != 128)
						return false;

					if ((data [0] != 'M') || (data [1] != 'Z'))
						return false;

					int offset = data [60] + (data [61] << 8) +
						(data [62] << 16) + (data [63] << 24);

					stream.Position = offset;

					data = new byte [28];
					if (stream.Read (data, 0, 28) != 28)
						return false;

					if ((data [0] != 'P') && (data [1] != 'E') &&
					    (data [2] != 0) && (data [3] != 0))
						return false;

					return true;
				}
			} catch {
				return false;
			}
		}

		internal ProcessStart (DebuggerOptions options)
		{
			if (options == null)
				throw new ArgumentException ();
			if ((options.File == null) || (options.File == ""))
				throw new ArgumentException ();

			this.options = options;
			this.cwd = options.WorkingDirectory;

			string mono_path = options.MonoPath != null ?
				options.MonoPath : MonoPath;

			if (IsMonoAssembly (options.InferiorArgs [0])) {
				LoadNativeSymbolTable = Options.LoadNativeSymbolTable;
				IsNative = false;

				ArrayList start_argv = new ArrayList ();
				start_argv.Add (mono_path);
				start_argv.Add ("--inside-mdb");
				if (options.JitOptimizations != "")
					start_argv.Add ("--optimize=" + options.JitOptimizations);
				if (options.JitArguments != null)
					start_argv.AddRange (options.JitArguments);

				this.argv = new string [options.InferiorArgs.Length + start_argv.Count];
				start_argv.CopyTo (this.argv, 0);
				options.InferiorArgs.CopyTo (this.argv, start_argv.Count);
			} else {
				LoadNativeSymbolTable = true;
				IsNative = true;

				this.argv = options.InferiorArgs;
			}

			base_dir = GetFullPath (Path.GetDirectoryName (argv [0]));

			SetupEnvironment ();
		}

		internal ProcessStart (DebuggerOptions options, int pid)
		{
			this.options = options;
			this.PID = pid;

			LoadNativeSymbolTable = true;
			IsNative = true;
		}

		internal ProcessStart (DebuggerOptions options, string core_file)
			: this (options)
		{
			this.CoreFile = core_file;
		}

		internal ProcessStart (ProcessStart parent, int pid)
		{
			this.PID = pid;

			this.options = parent.options;
			this.cwd = parent.cwd;
			this.argv = parent.argv;
			this.base_dir = parent.base_dir;

			SetupEnvironment ();
		}

		public void SetupApplication (string exe_file, string cwd, string[] cmdline_args)
		{
			this.cwd = cwd;

			cmdline_args [0] = exe_file;
			this.argv = cmdline_args;

			base_dir = GetFullPath (Path.GetDirectoryName (argv [0]));

			SetupEnvironment ();
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

		public string WorkingDirectory {
			get { return cwd; }
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
				hash [name] = value + ":" + value;
			}
		}

		protected void SetupEnvironment ()
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

			add_env_path (hash, "MONO_SHARED_HOSTNAME", "mdb");
			add_env_path (hash, "MONO_INSIDE_MDB", "1");

			ArrayList list = new ArrayList ();
			foreach (DictionaryEntry entry in hash) {
				string key = (string) entry.Key;
				string value = (string) entry.Value;

				list.Add (key + "=" + value);
			}

			envp = new string [list.Count];
			list.CopyTo (envp, 0);
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

		protected string print_argv (string[] argv)
		{
			if (argv == null)
				return "null";
			else
				return String.Join (":", argv);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2},{3},{4})",
					      GetType (), WorkingDirectory,
					      print_argv (argv), IsNative,
					      LoadNativeSymbolTable);
		}
	}
}
