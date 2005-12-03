using System;
using System.IO;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;

namespace Mono.Debugger
{
	[Serializable]
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

		public string RemoteHost = null;
		public string RemoteMono = null;
	}

	internal sealed class ProcessStart : MarshalByRefObject
	{
		public readonly string[] UserArguments;
		public readonly string[] UserEnvironment;
		public readonly bool IsNative;
		public readonly bool LoadNativeSymbolTable = true;
		public readonly string WorkingDirectory;

		string base_dir;
		string[] argv;
		string[] envp;
		DebuggerOptions options;

		public static string JitWrapper;

		static ProcessStart ()
		{
			string base_directory = System.AppDomain.CurrentDomain.BaseDirectory;

			/* Use relative path based on where Mono.Debugger.dll is at to enable relocation */
			JitWrapper = Path.GetFullPath (
				base_directory + Path.DirectorySeparatorChar +
				Path.DirectorySeparatorChar + "mono-debugger-mini-wrapper");
		}

		protected static bool IsMonoAssembly (string filename)
		{
			try {
				FileStream stream = new FileStream (filename, FileMode.Open);

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
			this.UserArguments = options.InferiorArgs;
			this.WorkingDirectory = options.WorkingDirectory;

			if (IsMonoAssembly (UserArguments [0])) {
				LoadNativeSymbolTable = Options.LoadNativeSymbolTable;
				IsNative = false;

				string[] start_argv = {
					JitWrapper, options.JitOptimizations
				};

				this.argv = new string [UserArguments.Length + start_argv.Length];
				start_argv.CopyTo (this.argv, 0);
				UserArguments.CopyTo (this.argv, start_argv.Length);
			} else {
				LoadNativeSymbolTable = true;
				IsNative = true;

				this.argv = UserArguments;
			}

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
			return String.Format ("{0} ({1},{2},{3},{4},{5})",
					      GetType (), WorkingDirectory,
					      print_argv (UserArguments),
					      print_argv (UserEnvironment),
					      IsNative, LoadNativeSymbolTable);
		}
	}
}
