using GLib;
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
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		string cwd;
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

		protected virtual string[] SetupArguments ()
		{
			if ((argv == null) || (argv.Length < 1))
				throw new Exception ("Invalid command line arguments");
			return argv;
		}

		protected virtual void DoSetup ()
		{
			SetupEnvironment ("PATH=" + Environment_Path, "LD_BIND_NOW=yes");
			SetupWorkingDirectory ();
			argv = SetupArguments ();
			load_native_symtab = true;
			native = true;
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

		protected override string[] SetupArguments ()
		{
			string[] old_argv = base.SetupArguments ();

			MethodInfo main = application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string[] start_argv = {
				Path_Mono, "--break", main_name, "--debug=mono",
				"--noinline", "--debug-args", "internal_mono_debugger",
				old_argv [0] };

			string[] new_argv = new string [old_argv.Length + start_argv.Length];
			start_argv.CopyTo (new_argv, 0);
			old_argv.CopyTo (new_argv, start_argv.Length);
			return new_argv;
		}
	}
}
