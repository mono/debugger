using System;
using System.IO;
using System.Collections;
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
		DebuggerBackend backend;
		ProcessStart start;

		public StartMode Mode {
			get { return start_mode; }
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		public ProcessStart ProcessStart {
			get { return start; }
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
		public string JitWrapper = null;

		[Option("JIT Optimizations", "jit-optimizations")]
		public string JitOptimizations = null;

		[Option("Display version and licensing information", 'V', "version")]
		public override WhatToDoNext DoAbout()
		{
			base.DoAbout ();
			return WhatToDoNext.AbandonProgram;
		}

		public string ParseArguments (string[] args)
		{
			ProcessArgs (args);
			args = RemainingArguments;

			switch (start_mode) {
			case StartMode.CoreFile:
				if (args.Length < 2)
					return "You need to specify at least the name of " +
						"the core file and the application it was " +
						"generated from.";

				string core_file = args [0];
				string [] temp_args = new string [args.Length-1];
				Array.Copy (args, 1, temp_args, 0, args.Length-1);
				args = temp_args;

				backend = new DebuggerBackend ();
				start = ProcessStart.Create (null, args, null, core_file);
				return null;

			case StartMode.LoadSession:
				if (args.Length != 1)
					return "This mode requires exactly one argument, " +
						"the file to load the session from.";

				StreamingContext context = new StreamingContext (
					StreamingContextStates.All, this);
				BinaryFormatter formatter = new BinaryFormatter (null, context);

				using (FileStream stream = new FileStream (args [0], FileMode.Open)) {
					backend = (DebuggerBackend) formatter.Deserialize (stream);
				}

				return null;

			case StartMode.Unknown:
				if (args.Length == 0)
					return null;

				backend = new DebuggerBackend ();
				start = ProcessStart.Create (null, args, null);
				return null;

			default:
				if (args.Length == 0)
					return "You need to specify the program you want " +
						"to debug.";

				backend = new DebuggerBackend ();
				start = ProcessStart.Create (null, args, null);
				return null;
			}
		}
	}
}
