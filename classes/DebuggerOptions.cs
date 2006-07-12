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
}
