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
		public readonly Interpreter Interpreter;

		public DebuggerEngine (Interpreter interpreter)
		{
			this.Interpreter = interpreter;
		}

		public ScriptingContext Context {
			get { return Interpreter.GlobalContext; }
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

		public abstract string Execute (Engine e);

		public virtual string Repeat (Engine e)
		{
			return Execute (e);
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

		public override string Execute (Engine e)
		{
			DebuggerEngine engine = (DebuggerEngine) e;

			try {
				if (!Resolve (engine.Context))
					return null;

				Execute (engine.Context);
			} catch (ThreadAbortException) {
			} catch (ScriptingException ex) {
				engine.Context.Error (ex);
				return null;
			} catch (Exception ex) {
				engine.Context.Error (
					"Caught exception while executing command {0}: {1}",
					this, ex);
				return null;
			}

			return "";
		}

		public override string Repeat (Engine e)
		{
			Repeating = true;
			return Execute (e);
		}

		protected Expression ParseExpression (ScriptingContext context)
		{
			if (Argument == "") {
				context.Error ("Argument expected");
				return null;
			}

			return DoParseExpression (context, Argument);
		}

		protected Expression DoParseExpression (ScriptingContext context, string arg)
		{
			IExpressionParser parser = context.Interpreter.GetExpressionParser (context, ToString());

			Expression expr = parser.Parse (arg);
			if (expr == null)
				context.Error ("Cannot parse arguments");

			return expr;
		}

		protected abstract void DoExecute (ScriptingContext context);

		protected virtual bool DoResolveBase (ScriptingContext context)
		{
			return true;
		}


		protected virtual bool DoResolve (ScriptingContext context)
		{
			if (Argument != "") {
				context.Error ("This command doesn't take any arguments");
				return false;
			}

			return DoResolveBase (context);
		}

		public bool Resolve (ScriptingContext context)
		{
			try {
				return DoResolve (context);
			} catch (ScriptingException ex) {
				context.Error (ex);
				return false;
			}
		}

		public void Execute (ScriptingContext context)
		{
			try {
				DoExecute (context);
			} catch (ScriptingException ex) {
				context.Error (ex);
			} catch (TargetException ex) {
				context.Error (ex.Message);
			}
		}
	}

	public abstract class ProcessCommand : DebuggerCommand
	{
		int process;

		public int Process {
			get { return process; }
			set { process = value; }
		}

		protected virtual ProcessHandle ResolveProcess (ScriptingContext context)
		{
			if (process > 0)
				return context.Interpreter.GetProcess (process);

			return context.CurrentProcess;
		}
	}

	public abstract class FrameCommand : ProcessCommand
	{
		int frame = -1;

		public int Frame {
			get { return frame; }
			set { frame = value; }
		}

		protected virtual FrameHandle ResolveFrame (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			context.ResetCurrentSourceCode ();
			if (frame > 0)
				return process.GetFrame (frame);
			else
				return process.CurrentFrame;
		}
	}

	public abstract class PrintCommand : FrameCommand
	{
		Expression expression;
		Format format = Format.Default;

		protected enum Format {
			Default = 0,
			Object,
			Current,
			Address
		};

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument.StartsWith ("/")) {
				int pos = Argument.IndexOfAny (new char[] { ' ', '\t' });
				string fstring = Argument.Substring (1, pos-1);
				string arg = Argument.Substring (pos + 1);

				switch (fstring) {
				case "o":
				case "object":
					format = Format.Object;
					break;

				case "c":
				case "current":
					format = Format.Current;
					break;

				case "a":
				case "address":
					format = Format.Address;
					break;

				default:
					throw new ScriptingException (
						"Unknown format: `{0}'", format);
				}

				expression = DoParseExpression (context, arg);
			} else
				expression = ParseExpression (context);

			if (expression == null)
				return false;

			expression = expression.Resolve (context);
			return expression != null;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			ResolveFrame (new_context);
			new_context.CurrentFrameIndex = Frame;

			Execute (new_context, expression, format);
		}

		protected abstract void Execute (ScriptingContext context,
						 Expression expression, Format format);
	}

	public class PrintExpressionCommand : PrintCommand, IDocumentableCommand
	{
		protected override void Execute (ScriptingContext context,
						 Expression expression, Format format)
		{
			switch (format) {
			case Format.Default: {
				object retval = expression.Evaluate (context);
				context.PrintObject (retval);
				break;
			}

			case Format.Object: {
				TargetObject obj = expression.EvaluateVariable (context);
				context.PrintObject (obj);
				break;
			}

			case Format.Current: {
				TargetObject obj = expression.EvaluateVariable (context);
				TargetClassObject cobj = obj as TargetClassObject;
				context.PrintObject (cobj);
				break;
			}

			case Format.Address: {
				TargetPointerObject obj = expression.EvaluateVariable (context)
					as TargetPointerObject;
				if (obj == null)
					context.Print ("<cannot take address>");
				else
					context.Print (obj.Address);
				break;
			}

			default:
				throw new InvalidOperationException ();
			}
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Data; } }
		public string Description { get { return "Print the result of an expression"; } }
		public string Documentation { get { return ""; } }
	}

	public class PrintTypeCommand : PrintCommand, IDocumentableCommand
	{
		protected override void Execute (ScriptingContext context,
						 Expression expression, Format format)
		{
			switch (format) {
			case Format.Default:
				TargetType type = expression.EvaluateType (context);
				context.PrintType (type);
				break;

			case Format.Object:
				TargetObject obj = expression.EvaluateVariable (context);
				context.PrintType (obj.Type);
				break;

			case Format.Current:
				obj = expression.EvaluateVariable (context);
				TargetClassObject cobj = obj as TargetClassObject;
				context.PrintType (cobj.Type);
				break;

			default:
				throw new InvalidOperationException ();
			}
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

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			ResolveFrame (new_context);
			new_context.CurrentFrameIndex = Frame;

			invocation.Invoke (new_context, true);
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

		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);
			ITargetAccess target = frame.Frame.TargetAccess;

			if (!Repeating) {
				PointerExpression pexp = expression as PointerExpression;
				if (pexp == null)
					throw new ScriptingException (
						"Expression `{0}' is not a pointer.",
						expression.Name);

				start = pexp.EvaluateAddress (context);
			}


			byte[] data = target.TargetMemoryAccess.ReadBuffer (start, count);
			context.Print (TargetBinaryReader.HexDump (start, data));
			start += count;
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Data; } }
		public string Description { get { return "Examine memory."; } }
		public string Documentation { get { return ""; } } 
	}

	public class PrintFrameCommand : ProcessCommand, IDocumentableCommand
	{
		int index = -1;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument == "")
				return true;

			try {
				index = (int) UInt32.Parse (Argument);
			} catch {
				context.Print ("Frame number expected.");
				return false;
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			context.ResetCurrentSourceCode ();

			if (index >= 0)
				process.CurrentFrameIndex = index;
			FrameHandle frame = process.CurrentFrame;

			if (context.Interpreter.IsScript)
				context.Print (frame);
			else
				context.Interpreter.Style.PrintFrame (context, frame);
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
		FrameHandle frame;
		TargetAddress address;
		IMethod method;
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

			frame = ResolveFrame (context);

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

			address = frame.Frame.TargetAddress;
			method = frame.Frame.Method;
			return true;
		}
	
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			if (do_method) {
				frame.DisassembleMethod (context, frame.Frame.TargetAccess);
				return;
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

					context.Error ("Reached end of current method.");
					return;
				}

				line = process.Process.DisassembleInstruction (method, address);
				if (line == null) {
					context.Error ("Cannot disassemble instruction at " +
						       "address {0}.", address);
					return;
				}

				context.PrintInstruction (line);
				address += line.InstructionSize;
			}
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
			if (Args != null && Args.Count != 1) {
				context.Error ("This command requires either zero or one argument");
				return false;
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (Args == null) {
				Console.WriteLine ("No executable file.");
				context.Interpreter.Options.File = null;
			}
			else {
				context.Interpreter.Options.File = (string)Args[0];
				Console.WriteLine ("Executable file: {0}.",
						   context.Interpreter.Options.File);
			}
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
			if (Argument != "") {
				context.Error ("This command doesn't take any arguments");
				return false;
			}
			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Console.WriteLine ("Working directory: {0}.",
					   context.Interpreter.Options.WorkingDirectory);
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
			if (Args == null) {
				context.Error ("Argument required (new working directory).");
				return false;
			}
			return true;
		}

		protected override void DoExecute (ScriptingContext context)
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

				Console.WriteLine ("Working directory {0}.",
						   context.Interpreter.Options.WorkingDirectory);
			}
			catch {
				Console.WriteLine ("{0}: No such file or directory.", Argument);
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

	public class SelectProcessCommand : DebuggerCommand, IDocumentableCommand
	{
		int index = -1;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument == "")
				return true;

			try {
				index = (int) UInt32.Parse (Argument);
			} catch {
				context.Print ("Process number expected.");
				return false;
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process;
			if (index >= 0)
				process = context.Interpreter.GetProcess (index);
			else
				process = context.CurrentProcess;

			context.CurrentProcess = process;
			context.Print (process);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Threads; } }
		public string Description { get { return "Print or select current process"; } }
		public string Documentation { get { return 
						"Without argument, print the current process.\n\n" +
						"With a process argument, make that process the current process.\n" +
						"This is the process which is used if you do not explicitly specify\n" +
						"a process (see `help process_expression' for details).\n"; } }
	}

	public class BackgroundProcessCommand : ProcessCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Background ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Threads; } }
		public string Description { get { return "Run process in background"; } }
		public string Documentation { get { return
						"Resumes execution of the selected process, but does not wait for it.\n\n" +
						"The difference to `continue' is that `continue' waits until the process\n" +
						"stops again (for instance, because it hit a breakpoint or received a signal)\n" +
						"while this command just lets the process running.  Note that the process\n" +
						"still stops if it hits a breakpoint.\n"; } }
	}

	public class StopProcessCommand : ProcessCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Stop ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Threads; } }
		public string Description { get { return "Stop execution of a process"; } }
		public string Documentation { get { return ""; } }
	}

	public class ContinueCommand : ProcessCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Continue);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Continue program being debugged."; } }
		public string Documentation { get { return ""; } }
	}

	public class StepCommand : ProcessCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Step);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Step program untli it reaches a different source line, proceeding into function calls."; } }
		public string Documentation { get { return ""; } }
	}

	public class NextCommand : ProcessCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Next);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Step program until it reaches a different source line, skipping over function calls."; } }
		public string Documentation { get { return ""; } }
	}

	public class StepInstructionCommand : ProcessCommand, IDocumentableCommand
	{
		bool native;

		public bool Native {
			get { return native; }
			set { native = value; }
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			if (Native)
				process.Step (WhichStepCommand.StepNativeInstruction);
			else
				process.Step (WhichStepCommand.StepInstruction);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Step program 1 instruction, but do not enter trampolines."; } }
		public string Documentation { get { return ""; } }
	}

	public class NextInstructionCommand : ProcessCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.NextInstruction);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Next instruction"; } }
		public string Documentation { get { return "Steps one machine instruction, but steps over method calls."; } }
	}

	public class FinishCommand : ProcessCommand, IDocumentableCommand
	{
		bool native;

		public bool Native {
			get { return native; }
			set { native = value; }
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			if (Native)
				process.Step (WhichStepCommand.FinishNative);
			else
				process.Step (WhichStepCommand.Finish);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Execute until selected stack frame returns."; } }
		public string Documentation { get { return ""; } }
	}

	public class BacktraceCommand : ProcessCommand, IDocumentableCommand
	{
		int max_frames = -1;

		public int Max {
			get { return max_frames; }
			set { max_frames = value; }
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			int current_idx = process.CurrentFrameIndex;
			BacktraceHandle backtrace = process.GetBacktrace (max_frames);
			context.ResetCurrentSourceCode ();

			for (int i = 0; i < backtrace.Length; i++) {
				string prefix = i == current_idx ? "(*)" : "   ";
				context.Print ("{0} {1}", prefix, backtrace [i]);
			}
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Stack; } }
		public string Description { get { return "Print backtrace of all stack frames."; } }
		public string Documentation { get { return ""; } }
	}

	public class UpCommand : ProcessCommand, IDocumentableCommand
	{
		int increment = 1;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args != null) {
				if (Args.Count == 1) {
					try {
						increment = (int) UInt32.Parse ((string)Args[0]);;
					} catch {
						context.Print ("Argument must be a positive integer");
						return false;
					}
				}
				else {
					context.Error ("At most one argument expected");
					return false;
				}
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex += increment;
			context.Interpreter.Style.PrintFrame (context, process.CurrentFrame);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Stack; } }
		public string Description { get { return "Select and print stack frame that called this one."; } }
		public string Documentation { get { return ""; } }
	}

	public class DownCommand : ProcessCommand, IDocumentableCommand
	{
		int decrement = 1;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args != null) {
				if (Args.Count == 1) {
					try {
						decrement = (int) UInt32.Parse ((string)Args[0]);;
					} catch {
						context.Print ("Argument must be a positive integer");
						return false;
					}
				}
				else {
					context.Error ("At most one argument expected");
					return false;
				}
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex -= decrement;
			context.Interpreter.Style.PrintFrame (context, process.CurrentFrame);
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
			if (Args == null && (context.Interpreter.Options.File == null
					     || context.Interpreter.Options.File == "")) {
				Console.WriteLine ("No executable file specified.\n" +
						   "Use the `file' command.");
				return false;
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

		protected override void DoExecute (ScriptingContext context)
		{
			if (Args != null) {
				string[] cmd_args = (string []) Args.ToArray (typeof (string));

				string[] argv = new string [cmd_args.Length + 1];
				cmd_args.CopyTo (argv, 1);

				/* store them for the next invocation of this command */
				context.Interpreter.Options.InferiorArgs = cmd_args;
			}

			context.Interpreter.Start ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Start debugged program."; } }
		public string Documentation { get { return ""; } }
	}

	public class KillCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Kill ();
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Kill the program being debugged."; } }
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
					Console.WriteLine ("Not confirmed.");
					return false;
				}
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Exit ();
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

			if (subcommand_type == null) {
				context.Error ("Syntax error");
				return false;
			}

			subcommand = (DebuggerCommand) Activator.CreateInstance (subcommand_type);

			ArrayList new_args = new ArrayList ();
			for (int i = 1; i < Args.Count; i++)
				new_args.Add (Args [i]);

			subcommand.Args = new_args;
			return subcommand.Resolve (context);
		}
	  
		protected override void DoExecute (ScriptingContext context)
		{
			subcommand.Execute (context);
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
		Expression expr;

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

			protected override void DoExecute (ScriptingContext context)
			{
				if (lang != null)
					context.Interpreter.CurrentLang = lang;
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

			protected override void DoExecute (ScriptingContext context)
			{
				context.Interpreter.Style = style;
				context.Print ("Current style interface: {0}",
					       context.Interpreter.Style.Name);
			}

			public override void Complete (Engine e, string text, int start, int end)
			{
				DebuggerEngine engine = (DebuggerEngine) e;
			  
				e.Completer.StringsCompleter (engine.Interpreter.GetStyleNames(), text, start, end);
			}
		}
#endregion

		public SetCommand ()
		{
			RegisterSubcommand ("lang", typeof (SetLangCommand));
			RegisterSubcommand ("style", typeof (SetStyleCommand));
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument != "") {
				Expression e = ParseExpression (context);
				if (e is AssignmentExpression) {
					expr = e.Resolve (context);
					return (expr != null);
				}
			}

			return base.DoResolve (context);
		}

		protected override void DoExecute (ScriptingContext context)
		{	  	
			if (expr != null) {
				expr.Evaluate (context);
			}
			else {
				base.DoExecute (context);
			}
		}


		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Support; } }
		public string Description { get { return "Set things."; } }
		public string Documentation { get { return String.Format ("valid args are:{0}\n", GetCommandList()); } }
	}

	public class ShowCommand : NestedCommand, IDocumentableCommand
	{
#region show subcommands
		private class ShowProcessesCommand : DebuggerCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				int current_id = -1;
				if (context.Interpreter.HasTarget)
					current_id = context.CurrentProcess.ID;

				bool printed_something = false;
				foreach (ProcessHandle proc in context.Interpreter.Processes) {
					string prefix = proc.ID == current_id ? "(*)" : "   ";
					context.Print ("{0} {1}", prefix, proc);
					printed_something = true;
				}

				if (!printed_something)
					context.Print ("No target.");
			}
		}

		private class ShowRegistersCommand : FrameCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				FrameHandle frame = ResolveFrame (context);

				IArchitecture arch = ResolveProcess (context).Process.Architecture;
				context.Print (arch.PrintRegisters (frame.Frame));
			}
		}

		private class ShowParametersCommand : FrameCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				FrameHandle frame = ResolveFrame (context);

				frame.ShowParameters (context);
			}
		}

		private class ShowLocalsCommand : FrameCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				FrameHandle frame = ResolveFrame (context);

				frame.ShowLocals (context);
			}
		}

		private class ShowModulesCommand : DebuggerCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				context.ShowModules ();
			}
		}

		private class ShowSourcesCommand : DebuggerCommand
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

				modules = context.GetModules (ids);
				return modules != null;
			}

			protected override void DoExecute (ScriptingContext context)
			{
				foreach (Module module in modules)
					context.ShowSources (module);
			}
		}

		private class ShowMethodsCommand : DebuggerCommand
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

				sources = context.GetSources (ids);
				return sources != null;
			}

			protected override void DoExecute (ScriptingContext context)
			{
				foreach (SourceFile source in sources)
					context.PrintMethods (source);
			}
		}

		private class ShowBreakpointsCommand : DebuggerCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				context.Interpreter.ShowBreakpoints ();
			}
		}

		private class ShowThreadGroupsCommand : DebuggerCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				context.Interpreter.ShowThreadGroups ();
			}
		}

		private class ShowFrameCommand : FrameCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				StackFrame frame = ResolveFrame (context).Frame;

				context.Print ("Stack level {0}, stack pointer at {1}, " +
					       "frame pointer at {2}.", frame.Level,
					       frame.StackPointer, frame.FrameAddress);
			}
		}

		private class ShowLangCommand : FrameCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				StackFrame frame = ResolveFrame (context).Frame;

				// if lang == auto, we should print out what it currently is, ala gdb's
				// The current source language is "auto; currently c".
				context.Print ("The current source language is \"{0}\". The ILanguage is \"{1}\"",
					       context.Interpreter.CurrentLangPretty, frame.Language.SourceLanguage(frame));
			}
		}

		private class ShowStyleCommand : DebuggerCommand
		{
			protected override bool DoResolve (ScriptingContext context)
			{
				return true;
			}

			protected override void DoExecute (ScriptingContext context)
			{
				context.Print ("Current style interface: {0}",
					       context.Interpreter.Style.Name);
			}
		}
#endregion

		public ShowCommand ()
		{
			RegisterSubcommand ("processes", typeof (ShowProcessesCommand));
			//			RegisterSubcommand ("procs", typeof (ShowProcessesCommand));
			RegisterSubcommand ("registers", typeof (ShowRegistersCommand));
			//			RegisterSubcommand ("regs", typeof (ShowRegistersCommand));
			RegisterSubcommand ("locals", typeof (ShowLocalsCommand));
			RegisterSubcommand ("parameters", typeof (ShowParametersCommand));
			//			RegisterSubcommand ("params", typeof (ShowParamsCommand));
			RegisterSubcommand ("breakpoints", typeof (ShowBreakpointsCommand));
			RegisterSubcommand ("modules", typeof (ShowModulesCommand));
			RegisterSubcommand ("threadgroups", typeof (ShowThreadGroupsCommand));
			RegisterSubcommand ("methods", typeof (ShowMethodsCommand));
			RegisterSubcommand ("sources", typeof (ShowSourcesCommand));
			RegisterSubcommand ("frame", typeof (ShowFrameCommand));
			RegisterSubcommand ("lang", typeof (ShowLangCommand));
			RegisterSubcommand ("style", typeof (ShowStyleCommand));
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

			protected override void DoExecute (ScriptingContext context)
			{
				context.Interpreter.CreateThreadGroup (Argument);
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

			protected override void DoExecute (ScriptingContext context)
			{
				context.Interpreter.DeleteThreadGroup (Argument);
			}
		}

		private class ThreadGroupAddCommand : DebuggerCommand
		{
			protected string name;
			protected ProcessHandle[] threads;

			protected override bool DoResolve (ScriptingContext context)
			{
				if ((Args == null) || (Args.Count < 2)) {
					context.Print ("Invalid arguments: Need the name of the " +
						       "thread group to operate on and one ore more " +
						       "processes");
					return false;
				}

				name = (string) Args [0];
				int[] ids = new int [Args.Count - 1];
				for (int i = 0; i < Args.Count - 1; i++) {
					try {
						ids [i] = (int) UInt32.Parse ((string) Args [i+1]);
					} catch {
						context.Print ("Invalid argument {0}: expected " +
							       "process id", i+1);
						return false;
					}
				}

				threads = context.Interpreter.GetProcesses (ids);
				return threads != null;
			}

			protected override void DoExecute (ScriptingContext context)
			{
				context.Interpreter.AddToThreadGroup (name, threads);
			}
		}

		private class ThreadGroupRemoveCommand : ThreadGroupAddCommand
		{
			protected override void DoExecute (ScriptingContext context)
			{
				context.Interpreter.AddToThreadGroup (name, threads);
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

	public abstract class EventHandleCommand : DebuggerCommand 
	{
		protected IEventHandle handle;

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
		protected override void DoExecute (ScriptingContext context)
		{
			if (handle != null) {
				handle.Enable (context.CurrentProcess.Process);
			}
			else {
				// enable all breakpoints
				foreach (IEventHandle h in context.Interpreter.Events)
					h.Enable (context.CurrentProcess.Process);
			}
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Breakpoints; } }
		public string Description { get { return "Enable breakpoint/catchpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public class BreakpointDisableCommand : EventHandleCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			if (handle != null) {
				handle.Disable (context.CurrentProcess.Process);
			}
			else {
				// enable all breakpoints
				foreach (IEventHandle h in context.Interpreter.Events)
					h.Disable (context.CurrentProcess.Process);
			}
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Breakpoints; } }
		public string Description { get { return "Disable breakpoint/catchpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public class BreakpointDeleteCommand : EventHandleCommand, IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			if (handle != null) {
				context.Interpreter.DeleteEvent (context.CurrentProcess, handle);
			}
			else {
				IEventHandle[] hs = context.Interpreter.Events;

				if (hs.Length == 0)
					return;

				if (!context.Interpreter.Query ("Delete all breakpoints?"))
					return;

				// delete all breakpoints
				foreach (IEventHandle h in context.Interpreter.Events)
					context.Interpreter.DeleteEvent (context.CurrentProcess, h);
			}
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Breakpoints; } }
		public string Description { get { return "Delete breakpoint/catchpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public abstract class SourceCommand : DebuggerCommand
	{
		int method_id = -1;
		bool all;
		LocationType type = LocationType.Method;
		protected SourceLocation location;

		public int ID {
			get { return method_id; }
			set { method_id = value; }
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

		public bool All {
			get { return all; }
			set { all = value; }
		}

		protected TargetFunctionType EvaluateMethod (ScriptingContext context)
		{
			Expression expr = ParseExpression (context);
			if (expr == null)
				return null;

			expr = expr.Resolve (context);
			if (expr == null)
				return null;

			try {
				return expr.EvaluateMethod (context, type, null);
			} catch (MultipleLocationsMatchException ex) {
				context.AddMethodSearchResult (ex.Sources, !all);
				return null;
			} catch (ScriptingException ex) {
				return null;
			}
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (ID > 0) {
				if (Argument != "") {
					context.Error ("Cannot specify both a method id " +
						       "and an expression.");
					return false;
				}

				if (All) {
					context.Error ("Cannot specify both a method id " +
						       "and -all.");
					return false;
				}

				SourceMethod method = context.GetMethodSearchResult (ID);
				location = new SourceLocation (method);
				return true;
			}

			int line;
			int pos = Argument.IndexOf (':');
			if (pos >= 0) {
				string filename = Argument.Substring (0, pos);
				try {
					line = (int) UInt32.Parse (Argument.Substring (pos+1));
				} catch {
					context.Error ("Expected filename:line");
					return false;
				}

				location = context.FindLocation (filename, line);
				return true;
			}

			if (Argument == "")
				return true;

			try {
				line = (int) UInt32.Parse (Argument);
			} catch {
				return false;
			}

			location = context.FindLocation (context.CurrentLocation, line);
			return location != null;
		}
	}

	public class ListCommand : SourceCommand, IDocumentableCommand
	{
		int lines = 10;
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

			if (Repeating){
				return true;
			} else
				return base.DoResolve (context);
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (Repeating)
				context.ListSourceCode (null, Lines * (reverse ? -1 : 1));
			else {
				context.ListSourceCode (location, Lines * (reverse ? -1 : 1));
			}
		}

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
		int process_id = -1;
		TargetFunctionType func;
		ProcessHandle process;
		ThreadGroup tgroup;

		public string Group {
			get { return group; }
			set { group = value; }
		}

		public int Process {
			get { return process_id; }
			set { process_id = value; }
		}

		bool ResolveMethod (ScriptingContext context)
		{
			func = EvaluateMethod (context);
			return func != null;
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			bool resolved = false;

			// only try to resolve the breakpoint if we're
			// currently running a process (and therefore
			// have symbols loaded)
			if (context.CurrentProcess != null) {
				try {
					if (All) {
						if (Argument == "" && context.NumMethodSearchResults == 0) {
							context.Error ("to use -all you must either specify a method or have previously done a search");
							return false;
						}
					}
					  
					resolved = base.DoResolve (context);
				}
				catch (ScriptingException ex) {
					context.Error (ex);
				}
			}

			if (!resolved)
				resolved = ResolveMethod (context);

			if (!resolved) {
				context.Error ("No such method: `{0}'", Argument);
				return false;
			}

			if (process_id > 0) {
				process = context.Interpreter.GetProcess (process_id);
				if (group == null)
					tgroup = process.ThreadGroup;
			} else
				process = context.CurrentProcess;

			if (tgroup == null)
				tgroup = context.Interpreter.GetThreadGroup (Group, false);

			if ((location == null) && (func == null))
				location = context.CurrentLocation;

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (All) {
				for (int i = 1; i <= context.NumMethodSearchResults; i ++) {
					SourceMethod method = context.GetMethodSearchResult (i);
					location = new SourceLocation (method);

					int index = context.Interpreter.InsertBreakpoint (
						process, tgroup, location);
					context.Print ("Breakpoint {0} at {1}", index, location.Name);
				}
				return;
			} else if (location != null) {
				int index = context.Interpreter.InsertBreakpoint (
					process, tgroup, location);
				context.Print ("Breakpoint {0} at {1}", index, location.Name);
			} else if (func != null) {
				int index = context.Interpreter.InsertBreakpoint (
					process, tgroup, func);
				context.Print ("Breakpoint {0} at {1}.{2}", index,
					       func.DeclaringType.Name, func.Name);
			} else {
				throw new ScriptingException ("Cannot insert breakpoint.");
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
				e.Completer.SymbolCompleter (text, start, end);
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
		FrameHandle frame;
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
			frame = ResolveFrame (context);

			TargetType exception_type = frame.Language.ExceptionType;
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

		protected override void DoExecute (ScriptingContext context)
		{
			int index = context.Interpreter.InsertExceptionCatchPoint (
				frame.Language, frame.Process, tgroup, type);
			context.Print ("Inserted catch point {0} for {1}", index, type.Name);
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

	public class DumpCommand : FrameCommand, IDocumentableCommand
	{
		Expression expression;
		string mode = "object";

		public string Mode {
			get { return mode; }
			set { mode = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			expression = ParseExpression (context);
			if (expression == null)
				return false;

			expression = expression.Resolve (context);
			return expression != null;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			ResolveFrame (new_context);
			new_context.CurrentFrameIndex = Frame;

			object retval = expression.Evaluate (new_context);
			switch (mode) {
			case "object":
				context.Dump (retval);
				break;
			}
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Internal; } }
		public string Description { get { return "Dump detailed information about an expression."; } }
		public string Documentation { get { return ""; } }
	}

	public class LibraryCommand : FrameCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count != 1)) {
				context.Error ("Filename argument expected");
				return false;
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.LoadLibrary (context.CurrentProcess.Process, Argument);
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
		public override string Execute (Engine e)
		{
			if (Args == null || Args.Count == 0) {
				Console.WriteLine ("List of families of commands:\n");
				Console.WriteLine ("aliases -- Aliases of other commands");

				foreach (CommandFamily family in Enum.GetValues(typeof (CommandFamily)))
					Console.WriteLine ("{0} -- {1}", family.ToString().ToLower(), e.CommandFamilyBlurbs[(int)family]);

				Console.WriteLine ("\n" + 
						   "Type \"help\" followed by a class name for a list of commands in that family.\n" +
						   "Type \"help\" followed by a command name for full documentation.\n");
			}
			else {
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
				}
				else if (args[0] == "aliases") {
					foreach (string cmd_name in e.Aliases.Keys) {
						Type cmd_type = (Type) e.Aliases[cmd_name];
						if (cmd_type.GetInterface ("IDocumentableCommand") != null) {
							IDocumentableCommand c = (IDocumentableCommand) Activator.CreateInstance (cmd_type);

							Console.WriteLine ("{0} -- {1}", cmd_name, c.Description);
						}
					}
				}
				else {
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
		public override string Execute (Engine e)
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

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			ResolveFrame (new_context);
			new_context.CurrentFrameIndex = Frame;

			TargetAddress address = pexpr.EvaluateAddress (context);
			ProcessHandle process = new_context.CurrentProcess;
			ISimpleSymbolTable symtab = process.Process.SimpleSymbolTable;
			if (symtab == null) {
				context.Print (
					"Cannot lookup address {0}: no symbol table loaded");
				return;
			}

			Symbol symbol = symtab.SimpleLookup (address, false);
			if (symbol == null)
				context.Print ("No method contains address {0}.", address);
			else
				context.Print ("Found method containing address {0}: {1}",
					       address, symbol);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Internal; } }
		public string Description { get { return "Print the method containing the address."; } }
		public string Documentation { get { return ""; } }
	}

	public class UnwindCommand : ProcessCommand, IDocumentableCommand
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

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);

			TargetAddress address = pexpr.EvaluateAddress (context);
			ProcessHandle process = new_context.CurrentProcess;

			Backtrace backtrace = process.Process.UnwindStack (address);
			StackFrame[] frames = backtrace.Frames;
			for (int i = 0; i < frames.Length; i++)
				context.Print ("{0} {1}", "   ", frames [i]);
		}

		  // IDocumentableCommand
		  public CommandFamily Family { get { return CommandFamily.Internal; } }
		  public string Description { get { return "Unwind the stack to a given address."; } }
		  public string Documentation { get { return ""; } }
	}

	public class ServerCommand : DebuggerCommand, IDocumentableCommand
	{
		protected override bool DoResolve (ScriptingContext context)
		{
			if (Args == null)
				throw new ScriptingException ("No executable file specified.");

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			string[] cmd_args = (string []) Args.ToArray (typeof (string));

			context.Interpreter.StartServer (cmd_args);
		}

		// IDocumentableCommand
		public CommandFamily Family { get { return CommandFamily.Running; } }
		public string Description { get { return "Start background server."; } }
		public string Documentation { get { return ""; } }
	}
}
