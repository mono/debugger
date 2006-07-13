
using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontend
{
	public class DebuggerEngine : Engine
	{
		public DebuggerEngine (Interpreter interpreter)
			: base (interpreter)
		{
			RegisterCommand ("pwd", typeof (PwdCommand));
			RegisterCommand ("cd", typeof (CdCommand));
			RegisterCommand ("print", typeof (PrintExpressionCommand));
			RegisterAlias   ("p", typeof (PrintExpressionCommand));
			RegisterCommand ("ptype", typeof (PrintTypeCommand));
			RegisterCommand ("call", typeof (CallCommand));
			RegisterCommand ("examine", typeof (ExamineCommand));
			RegisterAlias   ("x", typeof (ExamineCommand));
			RegisterCommand ("file", typeof (FileCommand));
			RegisterCommand ("frame", typeof (PrintFrameCommand));
			RegisterAlias   ("f", typeof (PrintFrameCommand));
			RegisterCommand ("disassemble", typeof (DisassembleCommand));
			RegisterAlias   ("dis", typeof (DisassembleCommand));
			RegisterCommand ("thread", typeof (SelectThreadCommand));
			RegisterCommand ("background", typeof (BackgroundThreadCommand));
			RegisterAlias   ("bg", typeof (BackgroundThreadCommand));
			RegisterCommand ("stop", typeof (StopThreadCommand));
			RegisterCommand ("continue", typeof (ContinueCommand));
			RegisterAlias   ("cont", typeof (ContinueCommand));
			RegisterAlias   ("c", typeof (ContinueCommand));
			RegisterCommand ("step", typeof (StepCommand));
			RegisterAlias   ("s", typeof (StepCommand));
			RegisterCommand ("next", typeof (NextCommand));
			RegisterAlias   ("n", typeof (NextCommand));
			RegisterCommand ("stepi", typeof (StepInstructionCommand));
			RegisterAlias   ("i", typeof (StepInstructionCommand));
			RegisterCommand ("nexti", typeof (NextInstructionCommand));
			RegisterAlias   ("t", typeof (NextInstructionCommand));
			RegisterCommand ("finish", typeof (FinishCommand));
			RegisterAlias   ("fin", typeof (FinishCommand));
			RegisterCommand ("backtrace", typeof (BacktraceCommand));
			RegisterAlias   ("bt", typeof (BacktraceCommand));
			RegisterAlias   ("where", typeof (BacktraceCommand));
			RegisterCommand ("up", typeof (UpCommand));
			RegisterCommand ("down", typeof (DownCommand));
			RegisterCommand ("kill", typeof (KillCommand));
			RegisterAlias   ("k", typeof (KillCommand));
			RegisterCommand ("detach", typeof (DetachCommand));
			RegisterCommand ("set", typeof (SetCommand));
			RegisterCommand ("show", typeof (ShowCommand));
			RegisterCommand ("info", typeof (ShowCommand)); /* for gdb users */
			RegisterCommand ("threadgroup", typeof (ThreadGroupCommand));
			RegisterCommand ("enable", typeof (BreakpointEnableCommand));
			RegisterCommand ("disable", typeof (BreakpointDisableCommand));
			RegisterCommand ("delete", typeof (BreakpointDeleteCommand));
			RegisterCommand ("list", typeof (ListCommand));
			RegisterAlias   ("l", typeof (ListCommand));
			RegisterCommand ("break", typeof (BreakCommand));
			RegisterAlias   ("b", typeof (BreakCommand));
			RegisterCommand ("catch", typeof (CatchCommand));
			RegisterCommand ("watch", typeof (WatchCommand));
			RegisterCommand ("quit", typeof (QuitCommand));
			RegisterAlias   ("q", typeof (QuitCommand));
			RegisterCommand ("dump", typeof (DumpCommand));
			RegisterCommand ("help", typeof (HelpCommand));
			RegisterCommand ("library", typeof (LibraryCommand));
			RegisterCommand ("run", typeof (RunCommand));
			RegisterAlias   ("r", typeof (RunCommand));
			RegisterCommand ("attach", typeof (AttachCommand));
			RegisterCommand ("core", typeof (OpenCoreFileCommand));
			RegisterCommand ("about", typeof (AboutCommand));
			RegisterCommand ("lookup", typeof (LookupCommand));
			RegisterCommand ("return", typeof (ReturnCommand));
			RegisterCommand ("save", typeof (SaveCommand));
			RegisterCommand ("load", typeof (LoadCommand));
			RegisterCommand ("module", typeof (ModuleCommand));
		}
	}

	public enum CommandFamily {
		Running,
		Breakpoints,
		Catchpoints,
		Threads,
		Stack,
		Files,
		Data,
		Internal,
		Obscure,
		Support
	}

	public interface IDocumentableCommand {
		/* the class of this command (breakpoint, running, threads, etc) */
		CommandFamily Family {get; }

		/* a short blurb */
		string Description {get; }

		/* a long blurb */
		string Documentation {get; }
	}

	public abstract class Command {
		public ArrayList Args;

		public string Argument {
			get {
				if (Args != null){
					string [] s = (string []) Args.ToArray (typeof (string));
					return String.Join (" ", s);
				} else
					return "";
			}
		}

		public abstract object Execute (Engine e);

		public virtual void Repeat (Engine e)
		{
			Execute (e);
		}

		/* override this to provide command specific completion */
                public virtual void Complete (Engine e, string text, int start, int end)
		{
			if (text.StartsWith ("-")) {
				/* do our super cool argument completion on the command's
				 * properties, if there are any.
				 */
				e.Completer.ArgumentCompleter (GetType(), text, start, end);
			}
			else {
				/* otherwise punt */
				e.Completer.NoopCompleter (text, start, end);
			}
                }
	}

	public abstract class DebuggerCommand : Command
	{
		protected bool Repeating;
		private bool Error;
		private ScriptingContext context;

		public override object Execute (Engine e)
		{
			DebuggerEngine engine = (DebuggerEngine) e;

			context = new ScriptingContext (engine.Interpreter);

			if (!Resolve (context)) {
				Error = true;
				return null;
			}

			return Execute (context);
		}

		public override void Repeat (Engine e)
		{
			Repeating = true;
			Execute (e);
		}

		protected Expression ParseExpression (ScriptingContext context)
		{
			if (Argument == "")
				throw new ScriptingException ("Argument expected");

			return DoParseExpression (context, Argument);
		}

		protected Expression DoParseExpression (ScriptingContext context, string arg)
		{
			IExpressionParser parser = context.Interpreter.GetExpressionParser (
				context, ToString());

			Expression expr = parser.Parse (arg);
			if (expr == null)
				throw new ScriptingException ("Cannot parse arguments");

			return expr;
		}

		protected abstract object DoExecute (ScriptingContext context);

		protected virtual bool DoResolveBase (ScriptingContext context)
		{
			return true;
		}

		protected virtual bool DoResolve (ScriptingContext context)
		{
			if (Argument != "")
				throw new ScriptingException ("This command doesn't take any arguments");

			return true;
		}

		public bool Resolve (ScriptingContext context)
		{
			if (!DoResolveBase (context))
				return false;

			return DoResolve (context);
		}

		public object Execute (ScriptingContext context)
		{
			return DoExecute (context);
		}
	}

	public abstract class ProcessCommand : DebuggerCommand
	{
		int index = -1;
		Process process;

		public int Process {
			get { return index; }
			set { index = value; }
		}

		protected override bool DoResolveBase (ScriptingContext context)
		{
			if (!base.DoResolveBase (context))
				return false;

			context.CurrentProcess = process = context.Interpreter.GetProcess (index);
			return true;
		}

		public Process CurrentProcess {
			get { return process; }
		}
	}

	public abstract class ThreadCommand : DebuggerCommand
	{
		int index = -1;
		Thread thread;

		public int Thread {
			get { return index; }
			set { index = value; }
		}

		protected override bool DoResolveBase (ScriptingContext context)
		{
			if (!base.DoResolveBase (context))
				return false;

			context.CurrentThread = thread = context.Interpreter.GetThread (index);
			return true;
		}

		public Thread CurrentThread {
			get { return thread; }
		}
	}

	public abstract class FrameCommand : ThreadCommand
	{
		int index = -1;
		StackFrame frame;
		Backtrace backtrace;

		public int Frame {
			get { return index; }
			set { index = value; }
		}

		protected override bool DoResolveBase (ScriptingContext context)
		{
			if (!base.DoResolveBase (context))
				return false;

			if (!CurrentThread.IsStopped)
				throw new TargetException (TargetError.NotStopped);

			backtrace = CurrentThread.GetBacktrace ();

			if (index == -1)
				frame = backtrace.CurrentFrame;
			else {
				if (index >= backtrace.Count)
					throw new ScriptingException ("No such frame: {0}", index);

				frame = backtrace [index];
			}

			context.CurrentFrame = frame;
			return true;
		}

		public StackFrame CurrentFrame {
			get { return frame; }
		}

		public Backtrace CurrentBacktrace {
			get { return backtrace; }
		}
	}

	public abstract class PrintCommand : FrameCommand
	{
		Expression expression;
		DisplayFormat format = DisplayFormat.Object;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument.StartsWith ("/")) {
				int pos = Argument.IndexOfAny (new char[] { ' ', '\t' });
				string fstring = Argument.Substring (1, pos-1);
				string arg = Argument.Substring (pos + 1);

				switch (fstring) {
				case "o":
				case "object":
					format = DisplayFormat.Object;
					break;

				case "a":
				case "address":
					format = DisplayFormat.Address;
					break;

				case "x":
				case "hex":
					format = DisplayFormat.HexaDecimal;
					break;

				case "default":
					format = DisplayFormat.Default;
					break;

				default:
					throw new ScriptingException (
						"Unknown format: `{0}'", fstring);
				}

				expression = DoParseExpression (context, arg);
			} else
				expression = ParseExpression (context);

			if (expression == null)
				return false;

			if (this is PrintTypeCommand) {
				Expression resolved = expression.TryResolveType (context);
				if (resolved != null) {
					expression = resolved;
					return true;
				}
			}

			expression = expression.Resolve (context);
			return expression != null;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			return Execute (context, expression, format);
		}

		protected abstract string Execute (ScriptingContext context,
						   Expression expression, DisplayFormat format);
	}

	public class PrintExpressionCommand : PrintCommand, IDocumentableCommand
	{
		protected override string Execute (ScriptingContext context,
						   Expression expression, DisplayFormat format)
		{
			object retval = expression.Evaluate (context);
			string text = context.FormatObject (retval, format);
			context.Print (text);
			return text;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Data; } }
		public string Description { get { return "Print the result of an expression"; } }
		public string Documentation { get { return ""; } }
	}

	public class PrintTypeCommand : PrintCommand, IDocumentableCommand
	{
		protected override string Execute (ScriptingContext context,
						   Expression expression, DisplayFormat format)
		{
			TargetType type = expression.EvaluateType (context);
			string text = context.FormatType (type);
			context.Print (text);
			return text;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Data; } }
		public string Description { get { return "Print the type of an expression."; } }
		public string Documentation { get { return ""; } } 
	}

	public class CallCommand : FrameCommand, IDocumentableCommand
	{
		InvocationExpression invocation;

		protected override bool DoResolve (ScriptingContext context)
		{
			Expression expr = ParseExpression (context);
			if (expr == null)
				return false;

			expr = expr.Resolve (context);
			if (expr == null)
				return false;

			invocation = expr as InvocationExpression;
			if (invocation == null)
				throw new ScriptingException (
					"Expression `{0}' is not a method.", expr.Name);

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			invocation.Invoke (context, true);
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Invoke a function in the program being debugged."; } }
		public string Documentation { get { return ""; } } 
	}

	public class ExamineCommand : FrameCommand, IDocumentableCommand
	{
		TargetAddress start;
		Expression expression;
		int count = 16;

		public int Count {
			get { return count; }
			set { count = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Repeating)
				return true;

			expression = ParseExpression (context);
			if (expression == null)
				return false;

			expression = expression.Resolve (context);
			return expression != null;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			if (!Repeating) {
				PointerExpression pexp = expression as PointerExpression;
				if (pexp == null)
					throw new ScriptingException (
						"Expression `{0}' is not a pointer.",
						expression.Name);

				start = pexp.EvaluateAddress (context);
			}

			byte[] data = CurrentThread.ReadBuffer (start, count);
			context.Print (TargetBinaryReader.HexDump (start, data));
			start += count;
			return data;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Data; } }
		public string Description { get { return "Examine memory."; } }
		public string Documentation { get { return ""; } } 
	}

	public class PrintFrameCommand : FrameCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			if (context.Interpreter.IsScript)
				context.Print (CurrentFrame);
			else
				context.Interpreter.Style.PrintFrame (context, CurrentFrame);
			return CurrentFrame;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Stack; } }
		public string Description { get { return "Select and print a stack frame."; } }
		public string Documentation { get { return ""; } } 
	}

	public class DisassembleCommand : FrameCommand, IDocumentableCommand
	{
		bool do_method;
		int count = -1;
		TargetAddress address;
		Method method;
		Expression expression;

		public bool Method {
			get { return do_method; }
			set { do_method = value; }
		}

		public int Count {
			get { return count; }
			set { count = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Repeating)
				return true;

			if (Argument != "") {
				if (count < 0)
					count = 1;

				expression = ParseExpression (context);
				if (expression == null)
					return false;

				expression = expression.Resolve (context);
				return expression != null;
			}

			if (count < 0)
				count = 10;

			address = CurrentFrame.TargetAddress;
			method = CurrentFrame.Method;
			return true;
		}
	
		protected override object DoExecute (ScriptingContext context)
		{
			Thread thread = CurrentThread;

			if (do_method) {
				Method method = CurrentFrame.Method;

				if ((method == null) || !method.IsLoaded)
					throw new ScriptingException (
						"Selected stack frame has no method.");

				AssemblerMethod asm = CurrentThread.DisassembleMethod (method);
				foreach (AssemblerLine insn in asm.Lines)
					context.Interpreter.PrintInstruction (insn);
				return null;
			}

			if (!Repeating && (expression != null)) {
				PointerExpression pexp = expression as PointerExpression;
				if (pexp == null)
					throw new ScriptingException (
						"Expression `{0}' is not a pointer.",
						expression.Name);

				address = pexp.EvaluateAddress (context);
			}

			AssemblerLine line;
			for (int i = 0; i < count; i++) {
				if ((method != null) && (address >= method.EndAddress)) {
					if (i > 0)
						break;

					throw new ScriptingException ("Reached end of current method.");
				}

				line = thread.DisassembleInstruction (method, address);
				if (line == null)
					throw new ScriptingException (
						"Cannot disassemble instruction at address {0}.",
						address);

				context.Interpreter.PrintInstruction (line);
				address += line.InstructionSize;
			}
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Disassemble current instruction or method."; } }
		public string Documentation { get { return ""; } } 
	}

	public class FileCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args != null && Args.Count != 1)
				throw new ScriptingException (
					"This command requires either zero or one argument");

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			if (Args == null) {
				Console.WriteLine ("No executable file.");
				context.Interpreter.Options.File = null;
				return null;
			}

			string file = (string) Args [0];
			if (!File.Exists (file))
				throw new TargetException (TargetError.CannotStartTarget,
							   "No such file or directory: `{0}'", file);

			context.Interpreter.Options.File = file;
			context.Print ("Executable file: {0}.", file);
			return file;
		}

                public override void Complete (Engine e, string text, int start, int end)
		{
			e.Completer.FilenameCompleter (text, start, end);
                }

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Specify a program to debug."; } }
		public string Documentation { get { return ""; } } 
	}

	public class PwdCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument != "")
				throw new ScriptingException (
					"This command doesn't take any arguments");

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			string pwd = context.Interpreter.Options.WorkingDirectory;
			context.Print ("Working directory: {0}.", pwd);
			return pwd;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Print working directory.  This is used for your program as well."; } }
		public string Documentation { get { return ""; } } 
	}

	public class CdCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args == null)
				throw new ScriptingException (
					"Argument required (new working directory).");

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			try {
				string new_dir;

				if (Argument == "..") {
					new_dir = new DirectoryInfo (Environment.CurrentDirectory).Parent.FullName;
				}
				else if (Argument == ".") {
					new_dir = new DirectoryInfo (Environment.CurrentDirectory).FullName;
				}
				else {
					new_dir = new DirectoryInfo (Argument).FullName;			    
				}

				Environment.CurrentDirectory = new_dir;
				context.Interpreter.Options.WorkingDirectory = new_dir;

				context.Print ("Working directory {0}.", new_dir);
				return new_dir;
			} catch {
				throw new ScriptingException ("{0}: No such file or directory.", Argument);
			}
		}

                public override void Complete (Engine e, string text, int start, int end)
		{
			e.Completer.FilenameCompleter (text, start, end);
                }

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Set working directory to DIR for debugger and program being debugged."; } }
		public string Documentation { get { return "The change does not take effect for the program being debugged\nuntil the next time it is started."; } } 
	}

	public class SelectThreadCommand : DebuggerCommand, IDocumentableCommand
	{
		int index = -1;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument == "")
				return true;

			try {
				index = (int) UInt32.Parse (Argument);
			} catch {
				context.Print ("Thread number expected.");
				return false;
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			Thread thread;
			if (index >= 0)
				thread = context.Interpreter.GetThread (index);
			else
				thread = context.Interpreter.CurrentThread;

			context.Interpreter.CurrentThread = thread;
			context.Print ("{0} ({1}:{2:x}) {3}", thread,
				       thread.PID, thread.TID, thread.State);
			return thread;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Threads; } }
		public string Description { get { return "Print or select current thread"; } }
		public string Documentation { get { return 
						"Without argument, print the current thread.\n\n" +
						"With a thread argument, make that thread the current thread.\n" +
						"This is the thread which is used if you do not explicitly specify\n" +
						"a thread (see `help thread_expression' for details).\n"; } }
	}

	public class BackgroundThreadCommand : ThreadCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			CurrentThread.Continue (true);
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Threads; } }
		public string Description { get { return "Run thread in background"; } }
		public string Documentation { get { return
						"Resumes execution of the selected thread, but does not wait for it.\n\n" +
						"The difference to `continue' is that `continue' waits until the thread\n" +
						"stops again (for instance, because it hit a breakpoint or received a signal)\n" +
						"while this command just lets the thread running.  Note that the thread\n" +
						"still stops if it hits a breakpoint.\n"; } }
	}

	public class StopThreadCommand : ThreadCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			CurrentThread.Stop ();
			if (context.Interpreter.IsSynchronous)
				context.Interpreter.Wait (CurrentThread);
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Threads; } }
		public string Description { get { return "Stop execution of a thread"; } }
		public string Documentation { get { return ""; } }
	}

	public abstract class SteppingCommand : ThreadCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			CommandResult result = DoStep (context);
			if (context.Interpreter.IsSynchronous)
				context.Interpreter.Wait (result);
			return null;

		}

		protected abstract CommandResult DoStep (ScriptingContext context);
	}

	public class ContinueCommand : SteppingCommand, IDocumentableCommand
	{
		protected override CommandResult DoStep (ScriptingContext context)
		{
			return CurrentThread.Continue ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Continue program being debugged."; } }
		public string Documentation { get { return ""; } }
	}

	public class StepCommand : SteppingCommand, IDocumentableCommand
	{
		protected override CommandResult DoStep (ScriptingContext context)
		{
			context.Interpreter.Style.IsNative = false;
			return CurrentThread.StepLine ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Step program untli it reaches a different source line, proceeding into function calls."; } }
		public string Documentation { get { return ""; } }
	}

	public class NextCommand : SteppingCommand, IDocumentableCommand
	{
		protected override CommandResult DoStep (ScriptingContext context)
		{
			context.Interpreter.Style.IsNative = false;
			return CurrentThread.NextLine ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Step program until it reaches a different source line, skipping over function calls."; } }
		public string Documentation { get { return ""; } }
	}

	public class StepInstructionCommand : SteppingCommand, IDocumentableCommand
	{
		bool native;

		public bool Native {
			get { return native; }
			set { native = value; }
		}

		protected override CommandResult DoStep (ScriptingContext context)
		{
			context.Interpreter.Style.IsNative = true;
			if (Native)
				return CurrentThread.StepNativeInstruction ();
			else
				return CurrentThread.StepInstruction ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Step program 1 instruction, but do not enter trampolines."; } }
		public string Documentation { get { return ""; } }
	}

	public class NextInstructionCommand : SteppingCommand, IDocumentableCommand
	{
		protected override CommandResult DoStep (ScriptingContext context)
		{
			context.Interpreter.Style.IsNative = true;
			return CurrentThread.NextInstruction ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Next instruction"; } }
		public string Documentation { get { return "Steps one machine instruction, but steps over method calls."; } }
	}

	public class FinishCommand : SteppingCommand, IDocumentableCommand
	{
		bool native;

		public bool Native {
			get { return native; }
			set { native = value; }
		}

		protected override CommandResult DoStep (ScriptingContext context)
		{
			return CurrentThread.Finish (Native);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Execute until selected stack frame returns."; } }
		public string Documentation { get { return ""; } }
	}

	public class BacktraceCommand : ThreadCommand, IDocumentableCommand
	{
		int max_frames = -1;

		public int Max {
			get { return max_frames; }
			set { max_frames = value; }
		}

		protected override object DoExecute (ScriptingContext context)
		{
			Backtrace backtrace = CurrentThread.GetBacktrace (max_frames);

			for (int i = 0; i < backtrace.Count; i++) {
				string prefix = i == backtrace.CurrentFrameIndex ? "(*)" : "   ";
				context.Print ("{0} {1}", prefix, backtrace [i]);
			}

			return backtrace;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Stack; } }
		public string Description { get { return "Print backtrace of all stack frames."; } }
		public string Documentation { get { return ""; } }
	}

	public class UpCommand : ThreadCommand, IDocumentableCommand
	{
		int increment = 1;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args != null) {
				if (Args.Count == 1) {
					try {
						increment = (int) UInt32.Parse ((string)Args[0]);;
					} catch {
						throw new ScriptingException (
							"Argument must be a positive integer");
					}
				} else {
					throw new ScriptingException ("At most one argument expected");
				}
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			Backtrace backtrace = CurrentThread.GetBacktrace ();
			try {
				backtrace.CurrentFrameIndex += increment;
			} catch {
				throw new ScriptingException ("Can't go up any further.");
			}
			context.Interpreter.Style.PrintFrame (context, backtrace.CurrentFrame);
			return backtrace.CurrentFrame;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Stack; } }
		public string Description { get { return "Select and print stack frame that called this one."; } }
		public string Documentation { get { return ""; } }
	}

	public class DownCommand : ThreadCommand, IDocumentableCommand
	{
		int decrement = 1;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args != null) {
				if (Args.Count == 1) {
					try {
						decrement = (int) UInt32.Parse ((string)Args[0]);;
					} catch {
						throw new ScriptingException (
							"Argument must be a positive integer");
					}
				} else {
					throw new ScriptingException ("At most one argument expected");
				}
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			Backtrace backtrace = CurrentThread.GetBacktrace ();
			try {
				backtrace.CurrentFrameIndex -= decrement;
			} catch {
				throw new ScriptingException ("Can't go down any further.");
			}
			context.Interpreter.Style.PrintFrame (context, backtrace.CurrentFrame);
			return backtrace.CurrentFrame;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Stack; } }
		public string Description { get { return "Select and print stack frame called by this one."; } }
		public string Documentation { get { return ""; } }
	}

	public class RunCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if ((context.Interpreter.Options.File == null) ||
			    (context.Interpreter.Options.File == "")) {
				throw new ScriptingException (
					"No executable file specified.\nUse the `file' command.");
			}

			if (context.HasBackend && context.Interpreter.IsInteractive) {
				if (context.Interpreter.Query ("The program being debugged has been started already.\n" +
							       "Start it from the beginning?")) {
					context.Interpreter.Kill ();
					return true;
				}
				else {
					return false;
				}
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			if (Args != null) {
				string[] cmd_args = (string []) Args.ToArray (typeof (string));

				string[] argv = new string [cmd_args.Length + 1];
				cmd_args.CopyTo (argv, 1);

				/* store them for the next invocation of this command */
				context.Interpreter.Options.InferiorArgs = cmd_args;
			}

			return context.Interpreter.Start ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Start debugged program."; } }
		public string Documentation { get { return ""; } }
	}

	public class AttachCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if (!context.HasBackend)
				return true;

			if (context.Interpreter.IsInteractive && context.Interpreter.Query (
				    "The program being debugged has been started already.\n" +
				    "Start it from the beginning?")) {
				context.Interpreter.Kill ();
				return true;
			} else {
				return false;
			}
		}

		protected override object DoExecute (ScriptingContext context)
		{
			int pid = Int32.Parse ((string) Args [0]);

			return context.Interpreter.Attach (pid);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Start debugged program."; } }
		public string Documentation { get { return ""; } }
	}

	public class OpenCoreFileCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if ((context.Interpreter.Options.File == null) ||
			    (context.Interpreter.Options.File == "")) {
				throw new ScriptingException (
					"No core file specified.\nUse the `file' command.");
			}

			if (context.HasBackend && context.Interpreter.IsInteractive) {
				if (context.Interpreter.Query ("The program being debugged has been started already.\n" +
							       "Start it from the beginning?")) {
					context.Interpreter.Kill ();
					return true;
				}
				else {
					return false;
				}
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			return context.Interpreter.OpenCoreFile ((string) Args [0]);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Start debugged program."; } }
		public string Documentation { get { return ""; } }
	}

	public class KillCommand : ProcessCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			context.Interpreter.Kill (CurrentProcess);
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Kill the selected process."; } }
		public string Documentation { get { return ""; } }
	}

	public class DetachCommand : ProcessCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			context.Interpreter.Detach (CurrentProcess);
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Detach the selected process from the debugger."; } }
		public string Documentation { get { return ""; } }
	}

	public class QuitCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if (context.HasBackend && context.Interpreter.IsInteractive) {
				if (context.Interpreter.Query ("The program is running.  Exit anyway?")) {
					return true;
				}
				else {
					context.Print ("Not confirmed.");
					return false;
				}
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			context.Interpreter.Exit ();
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Exit mdb."; } }
		public string Documentation { get { return ""; } }
	}

	public class NestedCommand : DebuggerCommand
	{
		protected DebuggerCommand subcommand;
		protected Hashtable subcommand_type_hash;

		public NestedCommand ()
		{
			subcommand_type_hash = new Hashtable();
		}

		protected void RegisterSubcommand (string name, Type t)
		{
			subcommand_type_hash.Add (name, t);
		}

		protected string GetCommandList ()
		{
			StringBuilder sb = new StringBuilder ("");

			foreach (string k in subcommand_type_hash.Keys) {
				sb.Append (" ");
				sb.Append (k);
			}

			return sb.ToString();
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count < 1)) {
				context.Print ("Need an argument:{0}", GetCommandList());
				return false;
			}

			Type subcommand_type = (Type)subcommand_type_hash[(string) Args[0]];

			if (subcommand_type == null)
				throw new ScriptingException ("Syntax error");

			subcommand = (DebuggerCommand) Activator.CreateInstance (subcommand_type);

			ArrayList new_args = new ArrayList ();
			for (int i = 1; i < Args.Count; i++)
				new_args.Add (Args [i]);

			DebuggerEngine engine = context.Interpreter.DebuggerEngine;
			subcommand = (DebuggerCommand) engine.ParseArguments (subcommand, new_args);
			if (subcommand == null)
				return false;

			return subcommand.Resolve (context);
		}
	  
		protected override object DoExecute (ScriptingContext context)
		{
			return subcommand.Execute (context);
		}

                public override void Complete (Engine e, string text, int start, int end)
		{
			// this doesn't quite work yet.  in the end it
			// should allow completion of subcommand arguments,
			// but for now it just offers completion of the
			// subcommand.

			if (Args != null && Args.Count > 0) {
				/* the user tabbed after the space in the
				 * arguments.  the first arg is meant to
				 * identify the subcommand, and the next
				 * args are the subcommand arguments.  so
				 * push this off to the subcommand's
				 * completer. */
				Type subcommand_type = (Type)subcommand_type_hash[(string) Args[0]];
				if (subcommand_type == null) {
					e.Completer.NoopCompleter (text, start, end);
				}
				else {
					/* copied from above */
					subcommand = (DebuggerCommand) Activator.CreateInstance (subcommand_type);
					ArrayList new_args = new ArrayList ();
					for (int i = 1; i < Args.Count; i++)
						new_args.Add (Args [i]);
					subcommand.Args = new_args;

					subcommand.Complete (e, text, start, end);
				}

				return;
			}

			if (subcommand_type_hash.Count == 0) {
				e.Completer.NoopCompleter (text, start, end);
			}
			else {
				string[] haystack = new string[subcommand_type_hash.Count];
				subcommand_type_hash.Keys.CopyTo (haystack, 0);
				e.Completer.StringsCompleter (haystack, text, start, end);
			}
                }
	}


	public class SetCommand : NestedCommand, IDocumentableCommand
	{
		AssignmentCommand assign;

#region set subcommands
		private class SetLangCommand : DebuggerCommand
		{
			protected string lang;

			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count != 1)) {
					context.Print ("Invalid argument: Need the name of the language");
					return false;
				}

				lang = (string) Args [0];
				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				if (lang != null)
					context.Interpreter.CurrentLang = lang;
				return null;
			}
		}

		private class SetArgsCommand : DebuggerCommand
		{
			protected override bool DoResolve (ScriptingContext context)
			{
				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				string[] args;
				if (Args == null)
					args = new string [0];
				else {
					args = new string [Args.Count];
					Args.CopyTo (args, 0);
				}

				context.Interpreter.Options.InferiorArgs = args;
				return args;
			}
		}

		private class SetEnvironmentCommand : DebuggerCommand
		{
			protected override bool DoResolve (ScriptingContext context)
			{
				if (Args == null)
					throw new ScriptingException (
						"Invalid argument: Need name of environment variable");

				if ((Args.Count < 1) || (Args.Count > 2))
					throw new ScriptingException (
						"Invalid argument: Expected `VARIABLE VALUE'");

				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				string var = (string) Args [0];
				string value = Args.Count == 2 ? (string) Args [1] : null;

				context.Interpreter.Options.SetEnvironment (var, value);
				return null;
			}
		}

		private class SetStyleCommand : DebuggerCommand
		{
			StyleBase style;

			protected override bool DoResolve (ScriptingContext context)
			{
				if (Argument == "") {
					context.Print ("Invalid argument: Need the name of the style");
					return false;
				}

				style = context.Interpreter.GetStyle (Argument);
				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				context.Interpreter.Style = style;
				context.Print ("Current style interface: {0}",
					       context.Interpreter.Style.Name);
				return null;
			}

			public override void Complete (Engine e, string text, int start, int end)
			{
				DebuggerEngine engine = (DebuggerEngine) e;
			  
				e.Completer.StringsCompleter (engine.Interpreter.GetStyleNames(), text, start, end);
			}
		}
#endregion

		private class AssignmentCommand : FrameCommand
		{
			string arg;
			Expression expr;

			public AssignmentCommand (string arg)
			{
				this.arg = arg;
			}

			protected override bool DoResolve (ScriptingContext context)
			{
				expr = DoParseExpression (context, arg);
				if (expr == null)
					return false;

				expr = expr.Resolve (context);
				return expr != null;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				return expr.Evaluate (context);
			}
		}

		public SetCommand ()
		{
			RegisterSubcommand ("env", typeof (SetEnvironmentCommand));
			RegisterSubcommand ("args", typeof (SetArgsCommand));
			RegisterSubcommand ("lang", typeof (SetLangCommand));
			RegisterSubcommand ("style", typeof (SetStyleCommand));
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument.IndexOf ('=') > 0) {
				assign = new AssignmentCommand (Argument);
				return assign.Resolve (context);
			}

			return base.DoResolve (context);
		}

		protected override object DoExecute (ScriptingContext context)
		{	  	
			if (assign != null)
				return assign.Execute (context);
			else
				return base.DoExecute (context);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Set things."; } }
		public string Documentation { get { return String.Format ("valid args are:{0}\n", GetCommandList()); } }
	}

	public class ShowCommand : NestedCommand, IDocumentableCommand
	{
#region show subcommands
		private class ShowArgumentsCommand : DebuggerCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				DebuggerOptions options;
				if (context.Interpreter.HasCurrentProcess)
					options = context.Interpreter.CurrentProcess.Session.Options;
				else
					options = context.Interpreter.Options;

				string[] args = options.InferiorArgs;
				if (args == null)
					args = new string [0];
				context.Print ("Target application:      {0}\n" +
					       "Command line arguments:  {1}\n" +
					       "Working directory:       {2}\n",
					       options.File, String.Join (" ", args),
					       options.WorkingDirectory);
				return args;
			}
		}

		private class ShowProcessesCommand : DebuggerCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				Process[] processes = context.Interpreter.Processes;
				if ((processes.Length > 0) && !context.Interpreter.HasCurrentProcess)
					context.Interpreter.CurrentProcess = processes [0];
				foreach (Process process in processes) {
					string prefix = process == context.Interpreter.CurrentProcess ?
						"(*)" : "   ";

					context.Print ("{0} Process {1}", prefix,
						       context.Interpreter.PrintProcess (process));
				}
				return null;
			}
		}

		private class ShowThreadsCommand : DebuggerCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				int current_id = -1;
				if (context.Interpreter.HasCurrentThread)
					current_id = context.Interpreter.CurrentThread.ID;

				bool printed_something = false;
				foreach (Process process in context.Interpreter.Processes) {
					context.Print ("Process {0}:",
						       context.Interpreter.PrintProcess (process));
					foreach (Thread proc in process.Threads) {
						string prefix = proc.ID == current_id ? "(*)" : "   ";
						context.Print ("{0} {1} ({2}:{3:x}) {4}", prefix, proc,
							       proc.PID, proc.TID, proc.State);
						printed_something = true;
					}
				}

				if (!printed_something)
					context.Print ("No target.");

				return null;
			}
		}

		private class ShowRegistersCommand : FrameCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				string regs = CurrentThread.PrintRegisters (CurrentFrame);
				context.Print (regs);
				return regs;
			}
		}

		private class ShowParametersCommand : FrameCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				if (CurrentFrame.Method == null)
					throw new ScriptingException (
						"Selected stack frame has no method.");

				TargetVariable[] param_vars = CurrentFrame.Method.Parameters;
				foreach (TargetVariable var in param_vars) {
					string msg = context.Interpreter.Style.PrintVariable (
						var, CurrentFrame);
					context.Interpreter.Print (msg);
				}

				return null;
			}
		}

		private class ShowLocalsCommand : FrameCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				if (CurrentFrame.Method == null)
					throw new ScriptingException (
						"Selected stack frame has no method.");

				TargetVariable[] local_vars = CurrentFrame.Locals;
				foreach (TargetVariable var in local_vars) {
					string msg = context.Interpreter.Style.PrintVariable (
						var, CurrentFrame);
					context.Interpreter.Print (msg);
				}

				return null;
			}
		}

		private class ShowModulesCommand : ProcessCommand
		{
			bool all;

			public bool All {
				get { return all; }
				set { all = value; }
			}

			protected override object DoExecute (ScriptingContext context)
			{
				Module[] modules = CurrentProcess.Modules;

				context.Print ("{0,4}  {1,-8} {2,5} {3,5} {4,5} {5}",
					       "Id", "Group", "load?", "step?", "sym?", "Name");

				for (int i = 0; i < modules.Length; i++) {
					Module module = modules [i];

					if (!all && module.HideFromUser)
						continue;

					context.Print ("{0,4}  {1,-8} {2,5} {3,5} {4,5} {5}",
						       module.ID, module.ModuleGroup.Name,
						       module.LoadSymbols ? "y " : "n ",
						       module.StepInto ? "y " : "n ",
						       module.SymbolsLoaded ? "y " : "n ",
						       module.Name);
				}

				return null;
			}
		}

		private class ShowSourcesCommand : ThreadCommand
		{
			protected string name;
			protected Module[] modules;

			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count < 1)) {
					context.Print ("Invalid arguments: Need one or more module " +
						       "ids to operate on");
					return false;
				}

				int[] ids = new int [Args.Count];
				for (int i = 0; i < Args.Count; i++) {
					try {
						ids [i] = (int) UInt32.Parse ((string) Args [i]);
					} catch {
						context.Print ("Invalid argument {0}: expected " +
							       "module id", i);
						return false;
					}
				}

				modules = context.Interpreter.GetModules (ids);
				return modules != null;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				foreach (Module module in modules)
					context.ShowSources (module);

				return null;
			}
		}

		private class ShowMethodsCommand : ThreadCommand
		{
			protected string name;
			protected SourceFile[] sources;

			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count < 1)) {
					context.Print ("Invalid arguments: Need one or more source " +
						       "file ids to operate on");
					return false;
				}

				int[] ids = new int [Args.Count];
				for (int i = 0; i < Args.Count; i++) {
					try {
						ids [i] = (int) UInt32.Parse ((string) Args [i]);
					} catch {
						context.Print ("Invalid argument {0}: expected " +
							       "source file id", i);
						return false;
					}
				}

				sources = context.Interpreter.GetSources (ids);
				return sources != null;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				foreach (SourceFile source in sources)
					context.PrintMethods (source);

				return null;
			}
		}

		private class ShowBreakpointsCommand : DebuggerCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				context.Interpreter.ShowBreakpoints ();
				return null;
			}
		}

		private class ShowThreadGroupsCommand : DebuggerCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				context.Interpreter.ShowThreadGroups ();
				return null;
			}
		}

		private class ShowFrameCommand : FrameCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				context.Print ("Stack level {0}, stack pointer at {1}, " +
					       "frame pointer at {2}.", CurrentFrame.Level,
					       CurrentFrame.StackPointer,
					       CurrentFrame.FrameAddress);
				return null;
			}
		}

		private class ShowLangCommand : FrameCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				// if lang == auto, we should print out what it currently is, ala gdb's
				// The current source language is "auto; currently c".
				context.Print ("The current source language is \"{0}\".",
					       CurrentFrame.Language.Name);
				return null;
			}
		}

		private class ShowStyleCommand : DebuggerCommand
		{
			protected override bool DoResolve (ScriptingContext context)
			{
				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				context.Print ("Current style interface: {0}",
					       context.Interpreter.Style.Name);
				return null;
			}
		}

		private class ShowLocationCommand : FrameCommand
		{
			Expression expression;

			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count != 1)) {
					context.Print ("Invalid arguments: expression expected");
					return false;
				}

				expression = ParseExpression (context);
				if (expression == null)
					return false;

				expression = expression.Resolve (context);
				return expression != null;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				TargetVariable var = expression.EvaluateVariable (context);

				string location = var.PrintLocation (CurrentFrame);
				if (location != null)
					context.Print ("{0} is a variable of type {1} stored at {2}",
						       var.Name, var.Type.Name, location);
				else
					context.Print ("{0} is a variable of type {1}",
						       var.Name, var.Type.Name);

				return null;
			}
		}
#endregion

		public ShowCommand ()
		{
			RegisterSubcommand ("args", typeof (ShowArgumentsCommand));
			RegisterSubcommand ("procs", typeof (ShowProcessesCommand));
			RegisterSubcommand ("processes", typeof (ShowProcessesCommand));
			RegisterSubcommand ("threads", typeof (ShowThreadsCommand));
			RegisterSubcommand ("registers", typeof (ShowRegistersCommand));
			RegisterSubcommand ("regs", typeof (ShowRegistersCommand));
			RegisterSubcommand ("locals", typeof (ShowLocalsCommand));
			RegisterSubcommand ("parameters", typeof (ShowParametersCommand));
			RegisterSubcommand ("params", typeof (ShowParametersCommand));
			RegisterSubcommand ("breakpoints", typeof (ShowBreakpointsCommand));
			RegisterSubcommand ("modules", typeof (ShowModulesCommand));
			RegisterSubcommand ("threadgroups", typeof (ShowThreadGroupsCommand));
			RegisterSubcommand ("methods", typeof (ShowMethodsCommand));
			RegisterSubcommand ("sources", typeof (ShowSourcesCommand));
			RegisterSubcommand ("frame", typeof (ShowFrameCommand));
			RegisterSubcommand ("lang", typeof (ShowLangCommand));
			RegisterSubcommand ("style", typeof (ShowStyleCommand));
			RegisterSubcommand ("location", typeof (ShowLocationCommand));
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Show things."; } }
		public string Documentation { get { return String.Format ("valid args are:{0}\n", GetCommandList()); } }
	}

	public class ThreadGroupCommand : NestedCommand, IDocumentableCommand
	{
#region threadgroup subcommands
		private class ThreadGroupCreateCommand : DebuggerCommand
		{
			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count != 1)) {
					context.Print ("Need exactly one argument: the name of " +
						       "the new thread group");
					return false;
				}

				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				context.Interpreter.CreateThreadGroup (Argument);
				return null;
			}
		}

		private class ThreadGroupDeleteCommand : DebuggerCommand
		{
			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count != 1)) {
					context.Print ("Need exactly one argument: the name of " +
						       "the thread group to delete");
					return false;
				}

				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				context.Interpreter.DeleteThreadGroup (Argument);
				return null;
			}
		}

		private class ThreadGroupAddCommand : DebuggerCommand
		{
			protected string name;
			protected Thread[] threads;

			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count < 2)) {
					context.Print ("Invalid arguments: Need the name of the " +
						       "thread group to operate on and one ore more " +
						       "threads");
					return false;
				}

				name = (string) Args [0];
				int[] ids = new int [Args.Count - 1];
				for (int i = 0; i < Args.Count - 1; i++) {
					try {
						ids [i] = (int) UInt32.Parse ((string) Args [i+1]);
					} catch {
						context.Print ("Invalid argument {0}: expected " +
							       "thread id", i+1);
						return false;
					}
				}

				threads = context.Interpreter.GetThreads (ids);
				return threads != null;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				context.Interpreter.AddToThreadGroup (name, threads);
				return null;
			}
		}

		private class ThreadGroupRemoveCommand : ThreadGroupAddCommand
		{
			protected override object DoExecute (ScriptingContext context)
			{
				context.Interpreter.AddToThreadGroup (name, threads);
				return null;
			}
		}
#endregion

		public ThreadGroupCommand ()
		{
			RegisterSubcommand ("create", typeof (ThreadGroupCreateCommand));
			RegisterSubcommand ("delete", typeof (ThreadGroupDeleteCommand));
			RegisterSubcommand ("add", typeof (ThreadGroupAddCommand));
			RegisterSubcommand ("remove", typeof (ThreadGroupRemoveCommand));
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Threads; } }
		public string Description { get { return "Manage thread groups."; } }
		public string Documentation { get { return String.Format ("valid args are:{0}", GetCommandList()); } }
	}

	public class ModuleCommand : ProcessCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count < 1)) {
				context.Print ("Argument expected.");
				return false;
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			string name = (string) Args [0];

			ModuleBase module = null;
			if (name.StartsWith ("@")) {
				name = name.Substring (1);
				module = CurrentProcess.Session.Config.GetModuleGroup (name);
				if (module == null)
					throw new ScriptingException ("No such module group `{0}'", name);
			} else {
				int index;
				try {
					index = (int) UInt32.Parse (name);
				} catch {
					context.Print ("Module number expected.");
					return false;
				}

				foreach (Module mod in CurrentProcess.Modules) {
					if (mod.ID == index) {
						module = mod;
						break;
					}
				}
			}

			if (module == null)
				throw new ScriptingException ("No such module `{0}'", name);

			for (int i = 1; i < Args.Count; i++) {
				string command = (string) Args [i];

				switch (command) {
				case "step":
					module.StepInto = true;
					break;
				case "nostep":
					module.StepInto = false;
					break;
				case "hide":
					module.HideFromUser = true;
					break;
				case "nohide":
					module.HideFromUser = false;
					break;
				case "load":
					module.LoadSymbols = true;
					break;
				case "noload":
					module.LoadSymbols = false;
					break;
				default:
					throw new ScriptingException ("Invalid module command `{0}'", command);
				}
			}

			context.Print ("{0}: {1} {2} {3}",
				       module.Name,
				       module.HideFromUser ? "hide" : "nohide",
				       module.StepInto ? "step" : "nostep",
				       module.LoadSymbols ? "load " : "noload");


			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Files; } }
		public string Description { get { return "Manage modules."; } }
		public string Documentation { get { return ""; } }
	}

	public abstract class EventHandleCommand : ThreadCommand 
	{
		protected Event handle;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args == null || Args.Count == 0)
				return true;

			int id;
			try {
				id = (int) UInt32.Parse (Argument);
			} catch {
				context.Print ("event number expected.");
				return false;
			}

			handle = context.Interpreter.GetEvent (id);
			return handle != null;
		}
	}

	public class BreakpointEnableCommand : EventHandleCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			if (handle != null) {
				handle.Enable (CurrentThread);
			} else {
				// enable all breakpoints
				foreach (Event h in CurrentThread.Process.Session.Events)
					h.Enable (CurrentThread);
			}

			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Breakpoints; } }
		public string Description { get { return "Enable breakpoint/catchpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public class BreakpointDisableCommand : EventHandleCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			if (handle != null) {
				handle.Disable (CurrentThread);
			} else {
				// enable all breakpoints
				foreach (Event h in CurrentThread.Process.Session.Events)
					h.Disable (CurrentThread);
			}

			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Breakpoints; } }
		public string Description { get { return "Disable breakpoint/catchpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public class BreakpointDeleteCommand : EventHandleCommand, IDocumentableCommand
	{
		protected override object DoExecute (ScriptingContext context)
		{
			if (handle != null) {
				CurrentThread.Process.Session.DeleteEvent (CurrentThread, handle);
			} else {
				Event[] hs = CurrentThread.Process.Session.Events;

				if (hs.Length == 0)
					return null;

				if (!context.Interpreter.Query ("Delete all breakpoints?"))
					return null;

				// delete all breakpoints
				foreach (Event h in CurrentThread.Process.Session.Events)
					CurrentThread.Process.Session.DeleteEvent (CurrentThread, h);
			}

			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Breakpoints; } }
		public string Description { get { return "Delete breakpoint/catchpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public abstract class SourceCommand : FrameCommand
	{
		LocationType type = LocationType.Method;
		SourceLocation location;

		public bool Ctor {
			get { return type == LocationType.Constructor; }
			set { type = LocationType.Constructor; }
		}

		public bool Get {
			get { return type == LocationType.PropertyGetter; }
			set { type = LocationType.PropertyGetter; }
		}

		public bool Set {
			get { return type == LocationType.PropertySetter; }
			set { type = LocationType.PropertySetter; }
		}

		public bool Add {
			get { return type == LocationType.EventAdd; }
			set { type = LocationType.EventAdd; }
		}

		public bool Remove {
			get { return type == LocationType.EventRemove; }
			set { type = LocationType.EventRemove; }
		}

		public bool Invoke {
			get { return type == LocationType.DelegateInvoke; }
			set { type = LocationType.DelegateInvoke; }
		}

		public SourceLocation Location {
			get {
				if (location == null)
					throw new ScriptingException ("Location is invalid.");

				return location;
			}
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			int line;
			int pos = Argument.IndexOf (':');
			if (pos >= 0) {
				string filename = Argument.Substring (0, pos);
				try {
					line = (int) UInt32.Parse (Argument.Substring (pos+1));
				} catch {
					throw new ScriptingException ("Expected filename:line");
				}

				location = context.FindLocation (filename, line);
				return true;
			}

			if (Argument == "") {
				location = context.CurrentLocation;
				return location != null;
			}

			try {
				line = (int) UInt32.Parse (Argument);
				location = context.FindLocation (context.CurrentLocation, line);
				return location != null;
			} catch {
			}

			MethodExpression mexpr;
			try {
				Expression expr = ParseExpression (context);
				if (expr == null)
					return false;

				mexpr = expr.ResolveMethod (context, type);
			} catch {
				mexpr = null;
			}

			if (mexpr != null)
				location = mexpr.EvaluateSource (context);
			else
				location = context.FindMethod (Argument);
			return location != null;
		}
	}

	public class ListCommand : SourceCommand, IDocumentableCommand
	{
		int lines = 20;
		bool reverse = false;
		
		public int Lines {
			get { return lines; }
			set { lines = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument == "-"){
				reverse = true;
				return true;
			}

			return base.DoResolve (context);
		}

		protected override object DoExecute (ScriptingContext context)
		{
			int count = Lines * (reverse ? -1 : 1);
			if (!Repeating) {
				SourceBuffer buffer;

				if (Location.HasSourceFile) {
					string filename = Location.SourceFile.FileName;
					buffer = context.FindFile (filename);
					if (buffer == null)
						throw new ScriptingException (
							"Cannot find source file `{0}'", filename);
				} else
					throw new ScriptingException (
						"Current location doesn't have any source code.");

				source_code = buffer.Contents;

				if (Location.HasMethod && !Location.HasLine)
					count = Location.Method.EndRow - Location.Method.StartRow + 4;

				if (count < 0)
					last_line = System.Math.Max (Location.Line + 2, 0);
				else 
					last_line = System.Math.Max (Location.Line - 2, 0);
			}

			int start;
			if (count < 0){
				start = System.Math.Max (last_line + 2 * count, 0);
				count = -count;
			} else 
				start = last_line;

			last_line = System.Math.Min (start + count, source_code.Length);

			if (start > last_line){
				int t = start;
				start = last_line;
				last_line = t;
			}

			StringBuilder sb = new StringBuilder ();
			for (int line = start; line < last_line; line++) {
				string text = String.Format ("{0,4} {1}", line+1, source_code [line]);
				context.Print (text);
				sb.Append (text);
			}

			return sb.ToString ();
		}

		int last_line = -1;
		string[] source_code = null;

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Files; } }
		public string Description {
			get { return "List source code."; }
		}

		public string Documentation {
			get {
				return  "Syntax: list [LOCATION]\n" +
					"Any of the standard way of representing a location are allowed\n" + 
					"in addition the symbol `-' can be used to list the source code\n" + 
					"backwards from the last point that was listed";
			}
		}
	}

	public class BreakCommand : SourceCommand, IDocumentableCommand
	{
		string group;
		bool global, local;
		int domain = 0;
		ThreadGroup tgroup;
		TargetAddress address = TargetAddress.Null;

		public string Group {
			get { return group; }
			set { group = value; }
		}

		public bool Global {
			get { return global; }
			set { global = value; }
		}

		public bool Local {
			get { return local; }
			set { local = value; }
		}

		public int Domain {
			get { return domain; }
			set { domain = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (global) {
				if (local)
					throw new ScriptingException (
						"Cannot use both -local and -global.");

				if (Group != null)
					throw new ScriptingException (
						"Cannot use both -group and -global.");

				tgroup = ThreadGroup.Global;
			} else if (local) {
				if (Group != null)
					throw new ScriptingException (
						"Cannot use both -group and -local.");

				tgroup = context.Interpreter.GetThreadGroup (Group, false);
			} else if (Group != null) {
				tgroup = context.Interpreter.GetThreadGroup (Group, false);
			} else {
				tgroup = ThreadGroup.Global;
			}

			bool resolved = base.DoResolve (context);
			if (resolved)
				return true;

			try {
				PointerExpression pexpr = ParseExpression (context) as PointerExpression;
				if (pexpr != null) {
					address = pexpr.EvaluateAddress (context);
					return true;
				}
			} catch {
			}

			throw new ScriptingException ("No such method: `{0}'", Argument);
		}

		protected override object DoExecute (ScriptingContext context)
		{
			if (!address.IsNull) {
				if (domain != 0)
					throw new ScriptingException (
						"Can't specifcy an appdomain when inserting a " +
						"breakpoint on an address");

				int index = context.Interpreter.InsertBreakpoint (
					context.CurrentThread, tgroup, address);
				context.Print ("Breakpoint {0} at {1}", index, address);
				return index;
			} else if (Location.HasFunction) {
				if (domain != 0)
					throw new ScriptingException (
						"Can't insert function breakpoints in " +
						"other application domains.");

				int index = context.Interpreter.InsertBreakpoint (
					context.CurrentThread, tgroup, Location.Function);
				context.Print ("Breakpoint {0} at {1}", index, Location.Name);
				return index;
			} else {
				int index = context.Interpreter.InsertBreakpoint (
					context.CurrentThread, tgroup, domain, Location);
				context.Print ("Breakpoint {0} at {1}", index, Location.Name);
				return index;
			}
		}

                public override void Complete (Engine e, string text, int start, int end)
		{
			if (text.StartsWith ("-")) {
				e.Completer.ArgumentCompleter (GetType(), text, start, end);
			}
			else if (text.IndexOf (Path.DirectorySeparatorChar) != -1) {
				/* attempt filename completion */
				e.Completer.FilenameCompleter (text, start, end);
			}
			else {
				/* attempt symbol name completion */
				// e.Completer.SymbolCompleter (context, text, start, end);
			}
                }

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Breakpoints; } }
		public string Description { get { return "Insert breakpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public class CatchCommand : FrameCommand, IDocumentableCommand
	{
		string group;
		ThreadGroup tgroup;
		TargetClassType type;

		public string Group {
			get { return group; }
			set { group = value; }
		}

		bool IsSubclassOf (TargetClassType type, TargetType parent)
		{
			while (type != null) {
				if (type == parent)
					return true;

				if (!type.HasParent)
					return false;

				type = type.ParentType;
			}

			return false;
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			Language language = CurrentFrame.Language;
			if (CurrentFrame.Language == null)
				throw new ScriptingException ("Current frame doesn't have a language.");

			TargetType exception_type = language.ExceptionType;
			if (exception_type == null)
				throw new ScriptingException ("Current language doesn't have any exceptions.");

			Expression expr = ParseExpression (context);
			if (expr == null)
				return false;

			expr = expr.ResolveType (context);
			if (expr == null)
				return false;

			type = expr.EvaluateType (context) as TargetClassType;
			if (!IsSubclassOf (type, exception_type))
				throw new ScriptingException ("Type `{0}' is not an exception type.", expr.Name);

			if (tgroup == null)
				tgroup = context.Interpreter.GetThreadGroup (Group, false);

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			int index = context.Interpreter.InsertExceptionCatchPoint (
				CurrentThread, tgroup, type);
			context.Print ("Inserted catch point {0} for {1}", index, type.Name);
			return index;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Catchpoints; } }
		public string Description { get { return "Stop execution when an exception is raised."; } }
		public string Documentation { get { return
						"Argument should be a subclass of the target\n" +
						"language's exception type.  This command causes\n" +
						"execution of the program to stop at the point where\n" +
						"the exception is thrown, so you can examine locals\n" +
						"in that particular stack frame"; } }
	}

	public class WatchCommand : FrameCommand, IDocumentableCommand
	{
		Expression expression;
		TargetAddress address;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Repeating)
				return true;

			expression = ParseExpression (context);
			if (expression == null)
				return false;

			expression = expression.Resolve (context);
			return expression != null;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			if (!Repeating) {
				PointerExpression pexp = expression as PointerExpression;
				if (pexp == null)
					throw new ScriptingException (
						"Expression `{0}' is not a pointer.",
						expression.Name);

				address = pexp.EvaluateAddress (context);
			}

			int index = context.Interpreter.InsertHardwareWatchPoint (CurrentThread, address);
			context.Print ("Hardware watchpoint {0} at {1}", index, address);
			return index;

		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Catchpoints; } }
		public string Description { get { return "Insert a hardware watchpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public class DumpCommand : NestedCommand, IDocumentableCommand
	{
		protected class DumpObjectCommand : FrameCommand, IDocumentableCommand
		{
			Expression expression;

			protected override bool DoResolve (ScriptingContext context)
			{
				expression = ParseExpression (context);
				if (expression == null)
					return false;

				expression = expression.Resolve (context);
				return expression != null;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				object retval = expression.Evaluate (context);
				context.Dump (retval);
				return retval;
			}

			// IDocumentableCommand
			public CommandFamily Family { get { return CommandFamily.Internal; } }
			public string Description { get { return "Dump detailed information about an expression."; } }
			public string Documentation { get { return ""; } }
		}

		protected class DumpLineNumberTableCommand : SourceCommand, IDocumentableCommand
		{
			protected override bool DoResolve (ScriptingContext context)
			{
				bool resolved = base.DoResolve (context);
				if (!resolved)
					throw new ScriptingException ("No such method: `{0}'", Argument);

				if (!Location.HasSourceFile)
					throw new ScriptingException ("Location invalid.");

				return true;
			}

			protected override object DoExecute (ScriptingContext context)
			{
				Method method = Location.Method.GetMethod (0);
				if ((method == null) || !method.HasSource)
					throw new ScriptingException ("Location invalid.");

				method.Source.DumpLineNumbers ();
				return null;
			}

			// IDocumentableCommand
			public CommandFamily Family { get { return CommandFamily.Internal; } }
			public string Description { get { return "Dump the line number table."; } }
			public string Documentation { get { return ""; } }
		}

		public DumpCommand ()
		{
			RegisterSubcommand ("object", typeof (DumpObjectCommand));
			RegisterSubcommand ("lnt", typeof (DumpLineNumberTableCommand));
		}


		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Internal; } }
		public string Description { get { return "Dump stuff."; } }
		public string Documentation { get { return ""; } }
	}

	public class LibraryCommand : FrameCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count != 1))
				throw new ScriptingException ("Filename argument expected");

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			context.LoadLibrary (CurrentThread, Argument);
			return null;
		}

                public override void Complete (Engine e, string text, int start, int end)
		{
			if (text.StartsWith ("-")) {
				e.Completer.ArgumentCompleter (GetType(), text, start, end);
			}
			else {
				/* attempt filename completion */
				e.Completer.FilenameCompleter (text, start, end);
			}
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Files; } }
		public string Description { get { return "Load a library."; } }
		public string Documentation { get { return ""; } }
	}

	public class HelpCommand : Command, IDocumentableCommand
	{
		public override object Execute (Engine e)
		{
			if (Args == null || Args.Count == 0) {
				Console.WriteLine ("List of families of commands:\n");
				Console.WriteLine ("aliases -- Aliases of other commands");

				foreach (CommandFamily family in Enum.GetValues(typeof (CommandFamily)))
					Console.WriteLine ("{0} -- {1}", family.ToString().ToLower(), e.CommandFamilyBlurbs[(int)family]);

				Console.WriteLine ("\n" + 
						   "Type \"help\" followed by a class name for a list of commands in that family.\n" +
						   "Type \"help\" followed by a command name for full documentation.\n");
			} else {
				string[] args = (string []) Args.ToArray (typeof (string));
				int family_index = -1;
				int i;

				string[] family_names = Enum.GetNames (typeof (CommandFamily));
				for (i = 0; i < family_names.Length; i ++) {
					if (family_names[i].ToLower() == args[0]) {
						family_index = i;
						break;
					}
				}

				if (family_index != -1) {
					ArrayList cmds = (ArrayList) e.CommandsByFamily [family_index];

					if (cmds == null) {
						Console.WriteLine ("No commands exist in that family");
						return null;
					}

					/* we're printing out a command family */
					Console.WriteLine ("List of commands:\n");
					foreach (string cmd_name in cmds) {
						Type cmd_type = (Type) e.Commands[cmd_name];
						IDocumentableCommand c = (IDocumentableCommand) Activator.CreateInstance (cmd_type);
						Console.WriteLine ("{0} -- {1}", cmd_name, c.Description);
					}

					Console.WriteLine ("\n" +
							   "Type \"help\" followed by a command name for full documentation.\n");
				}
				else if (e.Commands[args[0]] != null) {
					/* we're printing out a command */
					Type cmd_type = (Type) e.Commands[args[0]];
					if (cmd_type.GetInterface ("IDocumentableCommand") != null) {
						IDocumentableCommand c = (IDocumentableCommand) Activator.CreateInstance (cmd_type);

						Console.WriteLine (c.Description);
						if (c.Documentation != null && c.Documentation != String.Empty)
							Console.WriteLine (c.Documentation);
					}
					else {
						Console.WriteLine ("No documentation for command \"{0}\".", args[0]);
					}
				} else if (args[0] == "aliases") {
					foreach (string cmd_name in e.Aliases.Keys) {
						Type cmd_type = (Type) e.Aliases[cmd_name];
						if (cmd_type.GetInterface ("IDocumentableCommand") != null) {
							IDocumentableCommand c = (IDocumentableCommand) Activator.CreateInstance (cmd_type);

							Console.WriteLine ("{0} -- {1}", cmd_name, c.Description);
						}
					}
				} else {
					Console.WriteLine ("Undefined command \"{0}\".  try \"help\".", args[0]);
				}
			}

			return null;
		}

                public override void Complete (Engine e, string text, int start, int end) {
			/* arguments to the "help" command are commands
			 * themselves, so complete against them. */
			e.Completer.CommandCompleter (text, start, end);
                }

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Print list of commands."; } }
		public string Documentation { get { return ""; } }
	}

	public class AboutCommand : Command, IDocumentableCommand
	{
		public override object Execute (Engine e)
		{
			Console.WriteLine ("Mono Debugger (C) 2003, 2004 Novell, Inc.\n" +
					   "Written by Martin Baulig (martin@ximian.com)\n" +
					   "        and Chris Toshok (toshok@ximian.com)\n");
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Print copyright/authour info."; } }
		public string Documentation { get { return ""; } }
	}

	public class LookupCommand : FrameCommand, IDocumentableCommand
	{
		Expression expression;
		PointerExpression pexpr;

		protected override bool DoResolve (ScriptingContext context)
		{
			expression = ParseExpression (context);
			if (expression == null)
				return false;

			expression = expression.Resolve (context);
			if (expression == null)
				return false;

			pexpr = expression as PointerExpression;
			if (pexpr == null)
				throw new ScriptingException (
					"Expression `{0}' is not a pointer.",
					expression.Name);

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			TargetAddress address = pexpr.EvaluateAddress (context);

			Symbol symbol = CurrentThread.SimpleLookup (address, false);
			if (symbol == null)
				context.Print ("No method contains address {0}.", address);
			else
				context.Print ("Found method containing address {0}: {1}",
					       address, symbol);
			return symbol;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Internal; } }
		public string Description { get { return "Print the method containing the address."; } }
		public string Documentation { get { return ""; } }
	}

	public class ReturnCommand : ThreadCommand
	{
		bool unconfirmed;
		bool invocation;

		public bool Yes {
			get { return unconfirmed; }
			set { unconfirmed = value; }
		}

		public bool Invocation {
			get { return invocation; }
			set { invocation = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (context.Interpreter.IsInteractive && !unconfirmed) {
				if (context.Interpreter.Query ("Make the current stack frame return?")) {
					return true;
				} else {
					Console.WriteLine ("Not confirmed.");
					return false;
				}
			}

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			if (Invocation)
				CurrentThread.AbortInvocation ();
			else
				CurrentThread.Return (true);
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Make the current stack frame return."; } }
		public string Documentation { get { return ""; } }
	}

	public class SaveCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count != 1))
				throw new ScriptingException ("Filename argument required");

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			using (FileStream fs = new FileStream ((string) Args [0], FileMode.Create))
				context.Interpreter.SaveSession (fs);
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Test."; } }
		public string Documentation { get { return ""; } }
	}

	public class LoadCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count != 1))
				throw new ScriptingException ("Filename argument required");

			return true;
		}

		protected override object DoExecute (ScriptingContext context)
		{
			if (context.HasBackend)
				throw new ScriptingException ("Already have a target.");
			try {
				using (FileStream fs = new FileStream ((string) Args [0], FileMode.Open))
					context.Interpreter.LoadSession (fs);
			} catch {
				throw new ScriptingException ("Can't load session from `{0}'", Args [0]);
			}
			return null;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Test."; } }
		public string Documentation { get { return ""; } }
	}
}
