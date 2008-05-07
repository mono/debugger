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

using Mono.Debugger.Languages;
using Mono.Debugger.Backend;

namespace Mono.Debugger
{
	public class DebuggerOptions : DebuggerMarshalByRefObject
	{
		static int next_id = 0;
		public readonly int ID = ++next_id;

		string file;
		string[] inferior_args;
		string[] jit_arguments;
		DebugFlags? debug_flags;
		bool? stop_in_main;
		bool? start_target;

		/* The executable file we're debugging */
		public string File {
			get { return file; }
			set { file = value; }
		}

		/* argv[1...n] for the inferior process */
		public string[] InferiorArgs {
			get { return inferior_args; }
			set { inferior_args = value; }
		}

		/* JIT optimization flags affecting the inferior
		 * process */
		public string JitOptimizations {
			get; set;
		}

		public string[] JitArguments {
			get { return jit_arguments; }
			set { jit_arguments = value; }
		}

		/* The inferior process's working directory */
		public string WorkingDirectory {
			get; set;
		}

		/* true if we're running in a script */
		public bool IsScript {
			get; set;
		}

		/* true if we want to start the application immediately */
		public bool StartTarget {
			get { return start_target ?? false; }
			set { start_target = value; }
		}
	  
		/* the value of the -debug-flags: command line argument */
		public DebugFlags DebugFlags {
			get { return debug_flags ?? DebugFlags.None; }
			set { debug_flags = value; }
		}

		public bool HasDebugFlags {
			get { return debug_flags != null; }
		}

		public string DebugOutput {
			get; set;
		}

		/* non-null if the user specified the -mono-prefix
		 * command line argument */
		public string MonoPrefix {
			get; set;
		}

		/* non-null if the user specified the -mono command line argument */
		public string MonoPath {
			get; set;
		}

		public bool StopInMain {
			get { return stop_in_main ?? true; }
			set { stop_in_main = value; }
		}

		public bool StartXSP {
			get; set;
		}

		Hashtable user_environment;

		string[] clone (string[] array)
		{
			if (array == null)
				return null;
			string[] new_array = new string [array.Length];
			array.CopyTo (new_array, 0);
			return new_array;
		}

		Hashtable clone (Hashtable hash)
		{
			if (hash == null)
				return null;
			Hashtable new_hash = new Hashtable ();
			foreach (string key in hash.Keys)
				new_hash.Add (key, hash [key]);
			return new_hash;
		}

		public DebuggerOptions Clone ()
		{
			DebuggerOptions options = new DebuggerOptions ();
			options.file = file;
			options.inferior_args = clone (inferior_args);
			options.JitOptimizations = JitOptimizations;
			options.jit_arguments = clone (jit_arguments);
			options.WorkingDirectory = WorkingDirectory;
			options.IsScript = IsScript;
			options.start_target = start_target;
			options.debug_flags = debug_flags;
			options.DebugOutput = DebugOutput;
			options.MonoPrefix = MonoPrefix;
			options.MonoPath = MonoPath;
			options.user_environment = clone (user_environment);
			return options;
		}

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

		internal void GetSessionData (XmlElement root)
		{
			XmlElement file_e = root.OwnerDocument.CreateElement ("File");
			file_e.InnerText = file;
			root.AppendChild (file_e);

			if (InferiorArgs != null) {
				foreach (string arg in InferiorArgs) {
					XmlElement arg_e = root.OwnerDocument.CreateElement ("InferiorArgs");
					arg_e.InnerText = arg;
					root.AppendChild (arg_e);
				}
			}

			if (JitArguments != null) {
				foreach (string arg in JitArguments) {
					XmlElement arg_e = root.OwnerDocument.CreateElement ("JitArguments");
					arg_e.InnerText = arg;
					root.AppendChild (arg_e);
				}
			}

			if (JitOptimizations != null) {
				XmlElement opt_e = root.OwnerDocument.CreateElement ("JitOptimizations");
				opt_e.InnerText = JitOptimizations;
				root.AppendChild (opt_e);
			}
			if (WorkingDirectory != null) {
				XmlElement cwd_e = root.OwnerDocument.CreateElement ("WorkingDirectory");
				cwd_e.InnerText = WorkingDirectory;
				root.AppendChild (cwd_e);
			}
			if (MonoPrefix != null) {
				XmlElement prefix_e = root.OwnerDocument.CreateElement ("MonoPrefix");
				prefix_e.InnerText = MonoPrefix;
				root.AppendChild (prefix_e);
			}
			if (MonoPath != null) {
				XmlElement path_e = root.OwnerDocument.CreateElement ("MonoPath");
				path_e.InnerText = MonoPath;
				root.AppendChild (path_e);
			}
		}

		private DebuggerOptions ()
		{ }

		void append_array (ref string[] array, string value)
		{
			if (array == null) {
				array = new string [1];
				array [0] = value;
			} else {
				string[] new_array = new string [array.Length + 1];
				array.CopyTo (new_array, 0);
				new_array [array.Length] = value;
				array = new_array;
			}
		}

		internal DebuggerOptions (XPathNodeIterator iter)
		{
			while (iter.MoveNext ()) {
				switch (iter.Current.Name) {
				case "File":
					file = iter.Current.Value;
					break;
				case "InferiorArgs":
					append_array (ref inferior_args, iter.Current.Value);
					break;
				case "JitArguments":
					append_array (ref jit_arguments, iter.Current.Value);
					break;
				case "WorkingDirectory":
					WorkingDirectory = iter.Current.Value;
					break;
				case "MonoPrefix":
					MonoPrefix = iter.Current.Value;
					break;
				case "MonoPath":
					MonoPath = iter.Current.Value;
					break;
				default:
					throw new InternalError ();
				}
			}

			if (inferior_args == null)
				inferior_args = new string [0];
		}

		static void About ()
		{
			Console.WriteLine (
				"The Mono Debugger is (C) 2003-2007 Novell, Inc.\n\n" +
				"The debugger source code is released under the terms of the GNU GPL\n\n" +

				"For more information on Mono, visit the project Web site\n" +
				"   http://www.go-mono.com\n\n" +

				"The debugger was written by Martin Baulig and Chris Toshok");

			Environment.Exit (0);
		}

		static void Usage ()
		{
			Console.WriteLine (
				"Mono Debugger, (C) 2003-2007 Novell, Inc.\n" +
				"mdb [options] [exe-file]\n" +
				"mdb [options] exe-file [inferior-arguments ...]\n\n" +
				
				"   -debug-flags:PARAM        Sets the debugging flags\n" +
				"   -jit-arg:PARAM	      Additional argument for the inferior mono\n" +
				"   -jit-optimizations:PARAM  Set jit optimizations used on the inferior process\n" +
				"   -mono:PATH                Override the inferior mono\n" +
				"   -mono-prefix:PATH         Override the mono prefix\n" +
				"   -start                    Start inferior without halting in Main()\n" +
				"   -run                      Start inferior and stop in Main()\n" +
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

			for (i = 0; i < args.Length; i++) {
				string arg = args[i];

				if (arg == "")
					continue;

				if (!parsing_options) {
					i--;
					break;
				}

				if (arg.StartsWith ("-")) {
					if (ParseOption (options, arg, ref args, ref i,
							 ref parsing_options))
						continue;
					Usage ();
					Console.WriteLine ("Unknown argument: {0}", arg);
					Environment.Exit (1);
				} else if (arg.StartsWith ("/")) {
					string unix_opt = "-" + arg.Substring (1);
					if (ParseOption (options, unix_opt, ref args, ref i,
							 ref parsing_options))
						continue;
					Usage ();
					Console.WriteLine ("Unknown argument: {0}", arg);
					Environment.Exit (1);
				}

				options.File = arg;
				break;
			}

			if (args.Length > i) {
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
			return true;
		}

		void SetupXSP ()
		{
			file = BuildInfo.xsp;
			if (start_target == null)
				start_target = true;
			if (stop_in_main == null)
				stop_in_main = false;
		}

		static bool ParseOption (DebuggerOptions debug_options,
					 string option,
					 ref string [] args,
					 ref int i,
					 ref bool parsing_options)
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

			case "-start":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.StartTarget = true;
				debug_options.StopInMain = false;
				return true;

			case "-run":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.StartTarget = true;
				debug_options.StopInMain = true;
				return true;

#if ENABLE_KAHALO
			case "-xsp":
				if (ms_value != null) {
					Usage ();
					Environment.Exit (1);
				}
				debug_options.StartXSP = true;
				debug_options.SetupXSP ();
				parsing_options = false;
				return true;
#endif
			}

			return false;
		}
	}
}
