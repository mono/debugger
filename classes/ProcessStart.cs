using System;
using System.IO;
using System.Configuration;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using Mono.GetOptions;

namespace Mono.Debugger
{
	public class DebuggerOptions : Options
	{
		public DebuggerOptions ()
		{
			ParsingMode = OptionsParsingMode.Linux;
			EndOptionProcessingWithDoubleDash = true;
		}

		[Option("The command-line prompt", 'p', "prompt")]
		public string Prompt = "(mdb) ";

		[Option("Full path name of the JIT wrapper", "jit-wrapper")]
		public string JitWrapper = ProcessStart.JitWrapper;

		[Option("JIT Optimizations", "jit-optimizations")]
		public string JitOptimizations = "";

		[Option("Working directory", "working-directory")]
		public string WorkingDirectory = ".";

		[Option("Load native symtabs", "native-symtabs")]
		public bool LoadNativeSymbolTable = false;

		[Option("Running in a script", "script")]
		public bool IsScript = false;

		[Option("Display version and licensing information", 'V', "version")]
		public override WhatToDoNext DoAbout()
		{
			base.DoAbout ();
			return WhatToDoNext.AbandonProgram;
		}
	}

	[Serializable]
	public class ProcessStart
	{
		public string WorkingDirectory;
		public string[] Arguments;
		public string[] Environment;
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

			this.Arguments = argv;
			this.WorkingDirectory = options.WorkingDirectory;

			try {
				application = Assembly.LoadFrom (argv [0]);
			} catch {
				application = null;
			}

			if (application != null) {
				LoadNativeSymbolTable = Options.LoadNativeSymbolTable;
				IsNative = false;

				string[] start_argv = {
					options.JitWrapper, options.JitOptimizations
				};

				this.argv = new string [argv.Length + start_argv.Length];
				start_argv.CopyTo (this.argv, 0);
				argv.CopyTo (this.argv, start_argv.Length);
			} else {
				LoadNativeSymbolTable = true;
				IsNative = true;

				this.argv = argv;
			}
		}

		public DebuggerOptions Options {
			get { return options; }
		}

		public string[] CommandLineArguments {
			get { return argv; }
		}

		public string TargetApplication {
			get { return argv [0]; }
		}

		public string BaseDirectory {
			get { return base_dir; }
		}

		protected virtual void SetupEnvironment ()
		{
			ArrayList list = new ArrayList ();
			if (Environment != null)
				list.AddRange (Environment);
			list.Add ("LD_BIND_NOW=yes");

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
			string[] args = options.RemainingArguments;

			if (args.Length == 0)
				return null;

			return new ProcessStart (options, args);
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
					      print_argv (Arguments),
					      print_argv (Environment),
					      IsNative, LoadNativeSymbolTable);
		}
	}
}
