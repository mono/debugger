using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	/// <summary>
	///   This is a very simple command-line interpreter for the Mono Debugger.
	/// </summary>
	public class Interpreter
	{
		DebuggerBackend backend;
		TextWriter stdout, stderr;
		string last_command;
		string[] last_args;
		bool print_current_insn = false;

		StackFrame current_frame = null;
		StackFrame[] current_backtrace = null;
		int current_frame_idx = -1;

		// <summary>
		//   Create a new command interpreter for the debugger backend @backend.
		//   The interpreter sends its stdout to @stdout and its stderr to @stderr.
		// </summary>
		public Interpreter (DebuggerBackend backend, TextWriter stdout, TextWriter stderr)
		{
			this.backend = backend;
			this.stdout = stdout;
			this.stderr = stderr;

			backend.FramesInvalidEvent += new StackFrameInvalidHandler (frames_invalid);
			backend.FrameChangedEvent += new StackFrameHandler (frame_changed);
		}

		void frames_invalid ()
		{
			current_frame_idx = -1;
			current_frame = null;
			current_backtrace = null;
		}

		void frame_changed (StackFrame frame)
		{
			frames_invalid ();
		}

		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;


		public bool CanRepeatLastCommand {
			get {
				return last_command != null;
			}
		}

		public void ShowHelp ()
		{
			stderr.WriteLine ("Commands:");
			stderr.WriteLine ("  q, quit,exit     Quit the debugger");
			stderr.WriteLine ("  r, run           Start/continue the target");
			stderr.WriteLine ("  c, continue      Continue execution");
			stderr.WriteLine ("  i, stepi         single instruction step");
			stderr.WriteLine ("  t, nexti         next instruction step");
			stderr.WriteLine ("  abort            Abort the target");
			stderr.WriteLine ("  kill             Kill the target");
			stderr.WriteLine ("  s, step          Single-step");
			stderr.WriteLine ("  n, next          Single-step");
			stderr.WriteLine ("  finish           Continues till the end of the function");
			stderr.WriteLine ("  stop             Stops the program");
			stderr.WriteLine ("  core FILE        Reads a core file");
			stderr.WriteLine ("  reload           Reloads the current frame");
			stderr.WriteLine ("  params           Displays parameters");
			stderr.WriteLine ("  locals           Displays local variables");
			stderr.WriteLine ("  break LOC        Inserts breakpoint");
			stderr.WriteLine ("  modules          Displays modules loaded");
			stderr.WriteLine ("  f, frame [idx]   Print current stack frame / frame idx");
			stderr.WriteLine ("  bt, backtrace    Print backtrace");
			stderr.WriteLine ("  up               Go one frame up");
			stderr.WriteLine ("  down             Go one frame down");
		}

		public bool ProcessCommand (string line)
		{
			return ProcessCommand (line, true);
		}

		// <summary>
		//   Process one command and return true if we should continue processing
		//   commands, ie. until the "quit" command has been issued.
		// </summary>
		public bool ProcessCommand (string line, bool handle_exc)
		{
			if (line == "") {
				if (last_command == null)
					return true;

				try {
					return ProcessCommand (last_command, last_args);
				} catch (TargetException e) {
					Console.WriteLine (e);
					stderr.WriteLine (e);
					return true;
				}
			}

			string[] tmp_args = line.Split (' ', '\t');
			string[] args = new string [tmp_args.Length - 1];
			Array.Copy (tmp_args, 1, args, 0, tmp_args.Length - 1);
			string command = tmp_args [0];

			last_command = null;
			last_args = new string [0];

			if (!handle_exc)
				return ProcessCommand (tmp_args [0], args);

			try {
				return ProcessCommand (tmp_args [0], args);
			} catch (Exception e) {
				Console.WriteLine (e);
				stderr.WriteLine (e);
				return true;
			}
		}

		// <summary>
		//   Process one command and return true if we should continue processing
		//   commands, ie. until the "quit" command has been issued.
		// </summary>
		public bool ProcessCommand (string command, string[] args)
		{
			switch (command) {
			case "h":
			case "help":
				ShowHelp ();
				break;

			case "q":
			case "quit":
			case "exit":
				return false;

			case "c":
			case "continue":
				backend.CurrentProcess.Continue ();
				last_command = command;
				break;

			case "i":
			case "stepi":
				backend.CurrentProcess.StepInstruction ();
				last_command = command;
				break;

			case "t":
			case "nexti":
				backend.CurrentProcess.NextInstruction ();
				last_command = command;
				break;

			case "s":
			case "step":
				backend.CurrentProcess.StepLine ();
				last_command = command;
				break;

			case "n":
			case "next":
				backend.CurrentProcess.NextLine ();
				last_command = command;
				break;

			case "finish":
				backend.CurrentProcess.Finish ();
				break;

			case "f":
			case "frame":
				if (check_stopped ()) {
					if (current_frame == null)
						current_frame = backend.CurrentFrame;

					if (args.Length > 1) {
						stderr.WriteLine ("Command requires either one single " +
								  "argument or no argument at all.");
						break;
					}

					if (args.Length == 1) {
						try {
							current_frame_idx = (int) UInt32.Parse (args [0]);
						} catch {
							stderr.WriteLine ("Synax error.");
							break;
						}

						if (!check_frame (current_frame_idx)) {
							current_frame_idx = -1;
							break;
						}

						current_frame = current_backtrace [current_frame_idx];
					}

					PrintFrame (current_frame, current_frame_idx, true);
				}
				break;

			case "backtrace":
			case "bt":
				if (check_stopped ())
					PrintBacktrace ();
				break;

			case "down":
				if (!check_stopped ())
					break;

				if (current_frame_idx == -1)
					current_frame_idx = 0;

				if (check_frame (current_frame_idx - 1)) {
					current_frame = current_backtrace [--current_frame_idx];
					PrintFrame (current_frame, current_frame_idx, true);
					last_command = command;
				}
				break;

			case "up":
				if (!check_stopped ())
					break;

				if (current_frame_idx == -1)
					current_frame_idx = 0;

				if (check_frame (current_frame_idx + 1)) {
					current_frame = current_backtrace [++current_frame_idx];
					PrintFrame (current_frame, current_frame_idx, true);
					last_command = command;
				}
				break;

			case "regs":
				if (!check_stopped ())
					break;

				if (current_frame_idx > 0) {
					stderr.WriteLine ("Can't print registers of this frame.");
					break;
				}

				PrintRegisters ();
				break;

			case "reg":
				if (!check_stopped ())
					break;

				if (current_frame_idx > 0) {
					stderr.WriteLine ("Can't print registers of this frame.");
					break;
				}

				if (args.Length != 1) {
					stderr.WriteLine ("Command requires an argument.");
					break;
				}
				if (!args [0].StartsWith ("%")) {
					stderr.WriteLine ("Syntax error.");
					break;
				}

				PrintRegister (args [0].Substring (1));
				break;

			case "lnt":
			case "dump-lnt":
				dump_line_numbers (backend.CurrentFrame);
				break;

			case "clear-signal":
				backend.CurrentProcess.ClearSignal ();
				break;

			case "stop":
				backend.CurrentProcess.Stop ();
				break;

			case "sleep":
				Thread.Sleep (50000);
				break;

			case "reload":
				backend.Reload ();
				break;

			case "maps":
				foreach (TargetMemoryArea area in backend.GetMemoryMaps ())
					Console.WriteLine (area);
				break;
				
			case "params": {
				IVariable[] vars = backend.CurrentMethod.Parameters;
				foreach (IVariable var in vars) {
					Console.WriteLine ("PARAM: {0}", var);

					if (verbose)
						print_type (var.Type);

					if (!var.Type.HasObject)
						continue;

					try {
						ITargetObject obj = var.GetObject (backend.CurrentFrame);
						print_object (obj);
					} catch (LocationInvalidException) {
						Console.WriteLine ("CAN'T PRINT OBJECT");
						// Do nothing.
					}
				}
				break;
			}

			case "locals": {
				IVariable[] vars = backend.CurrentMethod.Locals;
				foreach (IVariable var in vars) {
					Console.WriteLine ("LOCAL: {0}", var);

					if (verbose)
						print_type (var.Type);

					if (!var.Type.HasObject)
						continue;

					try {
						ITargetObject obj = var.GetObject (backend.CurrentFrame);
						print_object (obj);
					} catch (Exception e) {
						Console.WriteLine ("CAN'T PRINT OBJECT: {0}", e);
					}
				}
				break;
			}

			case "verbose":
				verbose = !verbose;
				Console.WriteLine ("Verbose is {0}.", verbose);
				break;

			case "break":
				if (args.Length != 1) {
					stderr.WriteLine ("Command requires an argument");
					break;
				}
				Breakpoint breakpoint = new SimpleBreakpoint (args [0]);
				if (args [0].IndexOf (':') != -1) {
					string[] tmp = args [0].Split (':');
					string file = Path.GetFullPath (tmp [0]);
					int line = Int32.Parse (tmp [1]);
					backend.InsertBreakpoint (breakpoint, file, line);
				} else
					backend.InsertBreakpoint (breakpoint, args [0]);
				break;

			case "modules":
				Console.WriteLine ("MODULES:");
				print_modules (backend.Modules, 0);
				break;

			case "sources":
				Console.WriteLine ("SOURCES:");
				print_modules (backend.Modules, 1);
				break;

			case "methods":
				Console.WriteLine ("METHODS:");
				print_modules (backend.Modules, 2);
				break;

			default:
				stderr.WriteLine ("Unknown command: " + command);
				break;
			}

			return true;
		}

		bool check_stopped ()
		{
			switch (backend.State) {
			case TargetState.STOPPED:
			case TargetState.CORE_FILE:
				return true;

			case TargetState.RUNNING:
				stderr.WriteLine ("Target is running.");
				return false;

			case TargetState.BUSY:
				stderr.WriteLine ("Debugger is busy.");
				return false;

			default:
				stderr.WriteLine ("No target.");
				return false;
			}
		}

		bool check_frame (int idx)
		{
			if (current_backtrace == null)
				current_backtrace = backend.GetBacktrace ();
			if (current_backtrace == null) {
				stderr.WriteLine ("No backtrace.");
				return false;
			}

			if ((idx < 0) || (idx >= current_backtrace.Length)) {
				stderr.WriteLine ("No such frame.");
				return false;
			}

			return true;
		}

		public bool PrintCurrentInsn {
			get { return print_current_insn; }
			set { print_current_insn = value; }
		}

		public void PrintFrame (StackFrame frame, int index, bool print_insn)
		{
			if (index == -1)
				stdout.Write ("Stopped ");
			else
				stdout.Write ("#{0} ", index);

			IMethod method = frame.Method;
			if (method != null) {
				if (index != -1)
					stdout.Write ("at {0} ", frame.TargetAddress);

				stdout.Write ("in {0}", method.Name);
				if (method.IsLoaded) {
					long offset = frame.TargetAddress - method.StartAddress;
					if (offset > 0)
						stdout.Write ("+0x{0:x}", offset);
					else if (offset < 0)
						stdout.Write ("-0x{0:x}", -offset);
				}
			} else
				stdout.Write ("at {0}", frame.TargetAddress);

			if (frame.SourceLocation != null)
				stdout.Write (" at {0}", frame.SourceLocation.Name);
			stdout.WriteLine ();

			if (!print_current_insn || !print_insn)
				return;

			IDisassembler dis = backend.Disassembler;
			if (dis != null) {
				TargetAddress address = backend.CurrentFrameAddress;
				stdout.WriteLine ("{0:11x}\t{1}", address,
						  dis.DisassembleInstruction (ref address));
			}
		}

		public void PrintBacktrace ()
		{
			if (current_backtrace == null)
				current_backtrace = backend.GetBacktrace ();

			if (current_backtrace == null) {
				stderr.WriteLine ("No backtrace.");
				return;
			}

			for (int i = 0; i < current_backtrace.Length; i++)
				PrintFrame (current_backtrace [i], i, false);
		}

		public void PrintRegisters ()
		{
			IArchitecture arch = backend.Architecture;
			if (arch == null)
				return;

			foreach (int idx in arch.RegisterIndices)
				PrintRegister (idx);
		}

		public void PrintRegister (string name)
		{
			IArchitecture arch = backend.Architecture;
			if (arch == null)
				return;

			foreach (int idx in arch.RegisterIndices) {
				if (name == arch.RegisterNames [idx]) {
					PrintRegister (idx);
					return;
				}
			}

			stderr.WriteLine ("No such register.");
		}

		public void PrintRegister (int idx)
		{
			IArchitecture arch = backend.Architecture;
			if (arch == null)
				return;

			if (idx >= arch.RegisterNames.Length) {
				stderr.WriteLine ("No such register.");
				return;
			}

			long value = backend.GetRegister (idx);

			stdout.WriteLine ("%{0} = 0x{1}", arch.RegisterNames [idx],
					  TargetAddress.FormatAddress (value));
		}

		bool verbose = false;

		void print_array (ITargetArrayObject array, int dimension)
		{
			if (verbose) {
				Console.WriteLine ("  ARRAY DIMENSION {0}", dimension);
				Console.WriteLine ("  DYNAMIC CONTENTS: [{0}]",
						   TargetBinaryReader.HexDump (array.GetRawDynamicContents (-1)));
			}
			
			for (int i = array.LowerBound; i < array.UpperBound; i++) {
				if (verbose)
					Console.WriteLine ("    ELEMENT {0} {1}: {2}", dimension, i, array [i]);
				print_object (array [i]);
			}
		}

		void print_struct (ITargetStructObject tstruct)
		{
			if (verbose)
				Console.WriteLine ("  STRUCT: {0}", tstruct);
			foreach (ITargetFieldInfo field in tstruct.Type.Fields) {
				if (verbose)
					Console.WriteLine ("    FIELD: {0}", field);
				try {
					if (field.Type.HasObject)
						print_object (tstruct.GetField (field.Index));
				} catch {
					// Do nothing
				}
			}
			foreach (ITargetFieldInfo property in tstruct.Type.Properties) {
				Console.WriteLine ("    FIELD: {0}", property);
				try {
					if (property.Type.HasObject)
						print_object (tstruct.GetProperty (property.Index));
				} catch {
					// Do nothing
				}
			}
			try {
				Console.WriteLine ("PRINT: {0}", tstruct.PrintObject ());
			} catch (Exception e) {
				Console.WriteLine ("PrintObject: {0}", e);
			}
		}

		void print_class (ITargetClassObject tclass)
		{
			print_struct (tclass);

			ITargetClassObject parent = tclass.Parent;
			if (parent != null) {
				Console.WriteLine ("PARENT");
				print_class (parent);
			}
		}

		void print_pointer (ITargetPointerObject tpointer)
		{
			if (tpointer.Type.IsTypesafe && !tpointer.Type.HasStaticType && verbose)
				Console.WriteLine ("  CURRENTLY POINTS TO: {0}", tpointer.CurrentType);

			if (tpointer.CurrentType.HasObject) {
				if (verbose)
					Console.WriteLine ("  DEREFERENCED: {0}", tpointer.Object);
				ITargetObject tobject = tpointer.Object as ITargetObject;
				if (tobject != null)
					print_object (tobject);
			}
		}

		void print_type (ITargetType type)
		{
			ITargetArrayType array = type as ITargetArrayType;
			Console.WriteLine ("TYPE: {0}", type);
			if (array != null) {
				Console.WriteLine ("  IS AN ARRAY OF {0}.", array.ElementType);
				return;
			}

			ITargetClassType tclass = type as ITargetClassType;
			if ((tclass != null) && tclass.HasParent)
				Console.WriteLine ("  INHERITS FROM {0}.", tclass.ParentType);

			ITargetStructType tstruct = type as ITargetStructType;
			if (tstruct != null) {
				foreach (ITargetFieldInfo field in tstruct.Fields)
					Console.WriteLine ("  HAS FIELD: {0}", field);
				foreach (ITargetFieldInfo property in tstruct.Properties)
					Console.WriteLine ("  HAS PROPERTY: {0}", property);
				foreach (ITargetMethodInfo method in tstruct.Methods)
					Console.WriteLine ("  HAS METHOD: {0}", method);
				return;
			}

			ITargetPointerType tpointer = type as ITargetPointerType;
			if (tpointer != null) {
				Console.WriteLine ("  IS A {0}TYPE-SAFE POINTER.", tpointer.IsTypesafe ?
						   "" : "NON-");
				if (tpointer.HasStaticType)
					Console.WriteLine ("  POINTS TO {0}.", tpointer.StaticType);
			}

			if (type.HasObject)
				Console.WriteLine ("  HAS OBJECT");
		}

		void print_object (ITargetObject obj)
		{
			if (obj == null)
				return;

			Console.WriteLine ("  OBJECT: {0} [{1}]", obj,
					   TargetBinaryReader.HexDump (obj.RawContents));

			if (!obj.Type.HasFixedSize && verbose)
				Console.WriteLine ("  DYNAMIC CONTENTS: [{0}]",
						   TargetBinaryReader.HexDump (obj.GetRawDynamicContents (-1)));
			
			if (obj.HasObject)
				Console.WriteLine ("  OBJECT CONTENTS: |{0}|", obj.Object);

			ITargetArrayObject array = obj as ITargetArrayObject;
			if (array != null) {
				print_array (array, 0);
				return;
			}

			ITargetClassObject tclass = obj as ITargetClassObject;
			if (tclass != null) {
				print_class (tclass);
				return;
			}

			ITargetStructObject tstruct = obj as ITargetStructObject;
			if (tstruct != null) {
				print_struct (tstruct);
				return;
			}

			ITargetPointerObject tpointer = obj as ITargetPointerObject;
			if (tpointer != null) {
				print_pointer (tpointer);
				return;
			}
		}

		void print_modules (Module[] modules, int level)
		{
			if (modules == null)
				return;

			int module_count = 0;
			int source_count = 0;
			int method_count = 0;

			foreach (Module module in modules) {
				string language = module.Language != null ?
					module.Language.Name : "native";

				string library = "";
				ISymbolContainer container = module as ISymbolContainer;
				if (container != null) {
					if (module.IsLoaded && container.IsContinuous)
						library = String.Format ("{0} {1}", container.StartAddress,
									 container.EndAddress);
					else if (container.IsContinuous)
						library = "library";
				}

				Console.WriteLine ("  {0} {1} {2} {3} {4} {5} {6} {7}",
						   module.Name, module.FullName, language,
						   module.IsLoaded, module.SymbolsLoaded,
						   module.LoadSymbols, module.StepInto,
						   library);

				module_count++;

				foreach (Breakpoint breakpoint in module.Breakpoints)
					Console.WriteLine ("    BREAKPOINT: {0}", breakpoint);

				if ((module.Sources == null) || (level == 0))
					continue;

				foreach (SourceInfo source in module.Sources) {
					Console.WriteLine ("    SOURCE: {0}", source);

					++source_count;
					if (level == 1)
						continue;

					foreach (SourceMethodInfo method in source.Methods) {
						Console.WriteLine ("      METHOD: {0}", method);
						++method_count;
					}
				}
			}

			Console.WriteLine ("Total {0} modules, {1} source files and {2} methods.",
					   module_count, source_count, method_count);
		}

		void dump_line_numbers (StackFrame frame)
		{
			if ((frame.Method == null) || !frame.Method.HasSource)
				return;

			MethodSource source = frame.Method.Source as MethodSource;;
			if (source == null)
				return;

			source.DumpLineNumbers ();
		}
	}
}
