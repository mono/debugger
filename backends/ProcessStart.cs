using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.CSharp;
using Mono.Debugger.Architecture;

namespace Mono.Debugger
{
	public class ProcessStart
	{
		public readonly string Path_Mono		= "mono";
		public readonly string Environment_Path		= "/usr/bin";
		public readonly string Environment_LibPath	= "";

		string cwd;
		string base_dir;
		string[] argv;
		string[] envp;
		bool native;
		bool load_native_symtab;

		bool initialized = false;

		public ProcessStart (string cwd, string[] argv, string[] envp)
		{
			this.cwd = cwd;
			this.argv = argv;
			this.envp = envp;

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

				default:
					break;
				}
			}
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

		protected virtual void SetupEnvironment (params string[] add_envp)
		{
			ArrayList list = new ArrayList ();
			if (envp != null)
				list.AddRange (envp);
			if (add_envp != null)
				list.AddRange (add_envp);

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
	}

	public class ManagedProcessStart : ProcessStart
	{
		Assembly application;
		string[] old_argv;

		public ManagedProcessStart (string cwd, string[] argv, string[] envp, Assembly application)
			: base (cwd, argv, envp)
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

		protected override string[] SetupArguments ()
		{
			old_argv = base.SetupArguments ();

			MethodInfo main = application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string[] start_argv = { Path_Mono };

			string[] new_argv = new string [old_argv.Length + start_argv.Length];
			start_argv.CopyTo (new_argv, 0);
			old_argv.CopyTo (new_argv, start_argv.Length);
			return new_argv;
		}
	}
}
