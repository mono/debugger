using System;
using System.IO;
using System.Configuration;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using Mono.GetOptions;

namespace Mono.Debugger
{
	[Serializable]
	public class ProcessStart
	{
		public static string Path_Mono			= "mono";
		public static string JitOptimizations		= "";
		public static string Environment_Path		= "/usr/bin";
		public static string Environment_LibPath	= "";

		string cwd;
		string base_dir;
		string[] argv;
		string[] envp;
		string core_file;
		bool native;
		bool load_native_symtab;

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

		public ProcessStart (string cwd, string[] argv, string[] envp)
		{
			this.cwd = cwd;
			this.argv = argv;
			this.envp = envp;
		}

		public ProcessStart (string cwd, string[] argv, string[] envp, string core_file)
			: this (cwd, argv, envp)
		{
			this.core_file = core_file;
		}

		public string WorkingDirectory {
			get {
				if (!initialized)
					DoSetup ();

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

		public static ProcessStart Create (string cwd, string[] argv, string[] envp)
		{
			Assembly application;
			try {
				application = Assembly.LoadFrom (argv [0]);
			} catch (Exception e) {
				application = null;
			}

			if (application != null)
				return new ManagedProcessStart (cwd, argv, envp, application);
			else
				return new ProcessStart (cwd, argv, envp);
		}

		public static ProcessStart Create (string cwd, string[] argv, string[] envp, string core_file)
		{
			Assembly application;
			try {
				application = Assembly.LoadFrom (argv [0]);
			} catch (Exception e) {
				application = null;
			}

			if (application != null)
				return new ManagedProcessStart (cwd, argv, envp, application, core_file);
			else
				return new ProcessStart (cwd, argv, envp, core_file);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2},{3},{4},{5},{6},{7})", GetType (),
					      cwd, base_dir, argv, envp, native, load_native_symtab,
					      initialized);
		}
	}

	[Serializable]
	public class ManagedProcessStart : ProcessStart
	{
		Assembly application;
		string[] old_argv;

		public ManagedProcessStart (string cwd, string[] argv, string[] envp, Assembly application)
			: base (cwd, argv, envp)
		{
			this.application = application;
		}

		public ManagedProcessStart (string cwd, string[] argv, string[] envp, Assembly application,
					    string core_file)
			: base (cwd, argv, envp, core_file)
		{
			this.application = application;
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

			string[] start_argv = { Path_Mono, JitOptimizations };

			string[] new_argv = new string [old_argv.Length + start_argv.Length];
			start_argv.CopyTo (new_argv, 0);
			old_argv.CopyTo (new_argv, start_argv.Length);
			return new_argv;
		}
	}
}
