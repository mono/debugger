using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontends.Scripting
{
	public class DebuggerEngine : CL.Engine
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

	public abstract class DebuggerCommand : CL.Command
	{
		protected bool Repeating;

		protected virtual bool NeedsProcess {
			get { return true;
			}
		}

		public override string Execute (CL.Engine e)
		{
			DebuggerEngine engine = (DebuggerEngine) e;

			if (NeedsProcess && (engine.Context.CurrentProcess == null)) {
				engine.Context.Error ("No program to debug.", this, null);
				return null;
			}
			
			if (!Resolve (engine.Context))
				return null;

			try {
				Execute (engine.Context);
			} catch (Exception ex) {
				engine.Context.Error (
					"Caught exception while executing command {0}: {1}",
					this, ex);
				return null;
			}

			return "";
		}

		public override string Repeat (CL.Engine e)
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
			ExpressionParser parser = new ExpressionParser (context, ToString ());

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

	public class PrintExpressionCommand : PrintCommand, CL.IDocumentableCommand
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
				ITargetObject obj = expression.EvaluateVariable (context);
				context.PrintObject (obj);
				break;
			}

			case Format.Current: {
				ITargetObject obj = expression.EvaluateVariable (context);
				ITargetClassObject cobj = obj as ITargetClassObject;
				if (cobj != null)
					obj = cobj.CurrentObject;
				context.PrintObject (obj);
				break;
			}

			case Format.Address: {
				ITargetObject obj = expression.EvaluateVariable (context);
				context.Print (obj.Location.ReadAddress ());
				break;
			}

			default:
				throw new InvalidOperationException ();
			}
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Data; } }
		public string Description { get { return "Print the result of an expression"; } }
		public string Documentation { get { return ""; } }
	}

	public class PrintTypeCommand : PrintCommand, CL.IDocumentableCommand
	{
		protected override void Execute (ScriptingContext context,
						 Expression expression, Format format)
		{
			switch (format) {
			case Format.Default:
				ITargetType type = expression.EvaluateType (context);
				context.PrintType (type);
				break;

			case Format.Object:
				ITargetObject obj = expression.EvaluateVariable (context);
				context.PrintType (obj.Type);
				break;

			case Format.Current:
				obj = expression.EvaluateVariable (context);
				ITargetClassObject cobj = obj as ITargetClassObject;
				if (cobj != null)
					obj = cobj.CurrentObject;
				context.PrintType (obj.Type);
				break;

			default:
				throw new InvalidOperationException ();
			}
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Data; } }
		public string Description { get { return "Print the type of an expression."; } }
		public string Documentation { get { return ""; } } 
	}

	public class CallCommand : FrameCommand, CL.IDocumentableCommand
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Invoke a function in the program being debugged."; } }
		public string Documentation { get { return ""; } } 
	}

	public class StyleCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		Style style;

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Argument != "")
				style = context.Interpreter.GetStyle (Argument);
			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (style != null)
				context.Interpreter.Style = style;
			else
				context.Print ("Current style interface: {0}",
					       context.Interpreter.Style.Name);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Support; } }
		public string Description { get { return "Set or display the current output style."; } }
		public string Documentation { get { return ""; } } 
	}

	public class ExamineCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		TargetAddress start;
		ITargetAccess target;
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
			byte[] data;

			if (!Repeating) {
				PointerExpression pexp = expression as PointerExpression;
				if (pexp == null)
					throw new ScriptingException (
						"Expression `{0}' is not a pointer.",
						expression.Name);

				TargetLocation location = pexp.EvaluateAddress (context);

				start = location.Address;
				target = location.TargetAccess;
			}

			data = target.ReadBuffer (start, count);
			context.Print (TargetBinaryReader.HexDump (start, data));
			start += count;
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Data; } }
		public string Description { get { return "Examine memory."; } }
		public string Documentation { get { return ""; } } 
	}

	public class PrintFrameCommand : ProcessCommand, CL.IDocumentableCommand
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Stack; } }
		public string Description { get { return "Select and print a stack frame."; } }
		public string Documentation { get { return ""; } } 
	}

	public class DisassembleCommand : FrameCommand, CL.IDocumentableCommand
	{
		bool do_method;
		int count = -1;
		FrameHandle frame;
		TargetAddress address;
		IMethod method;
		IDisassembler dis;
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
			dis = context.CurrentProcess.Process.Disassembler;

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
			if (do_method) {
				frame.DisassembleMethod (context);
				return;
			}

			if (!Repeating && (expression != null)) {
				PointerExpression pexp = expression as PointerExpression;
				if (pexp == null)
					throw new ScriptingException (
						"Expression `{0}' is not a pointer.",
						expression.Name);

				TargetLocation location = pexp.EvaluateAddress (context);

				address = location.Address;
			}

			AssemblerLine line;
			for (int i = 0; i < count; i++) {
				if ((method != null) && (address >= method.EndAddress)) {
					if (i > 0)
						break;

					context.Error ("Reached end of current method.");
					return;
				}

				line = dis.DisassembleInstruction (method, address);
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Support; } }
		public string Description { get { return "Disassemble current instruction or method."; } }
		public string Documentation { get { return ""; } } 
	}

	public class StartCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		protected override bool NeedsProcess {
			get { return false; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count < 1)) {
				context.Error ("Filename and arguments expected");
				return false;
			}

			if (context.Interpreter.HasBackend) {
				context.Error ("Already have a target.");
				return false;
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			string [] args = (string []) Args.ToArray (typeof (string));

			try {
				DebuggerOptions options = new DebuggerOptions ();

				context.Interpreter.Start (options, args);
				context.Interpreter.Initialize ();
				context.Interpreter.Run ();
			} catch (TargetException e) {
				context.Interpreter.Kill ();
				throw new ScriptingException (e.Message);
			}
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Specify a program to debug."; } }
		public string Documentation { get { return ""; } } 
	}

	public class SelectProcessCommand : DebuggerCommand, CL.IDocumentableCommand
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Threads; } }
		public string Description { get { return "Print or select current process"; } }
		public string Documentation { get { return 
						"Without argument, print the current process.\n\n" +
						"With a process argument, make that process the current process.\n" +
						"This is the process which is used if you do not explicitly specify\n" +
						"a process (see `help process_expression' for details).\n"; } }
	}

	public class BackgroundProcessCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Background ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Threads; } }
		public string Description { get { return "Run process in background"; } }
		public string Documentation { get { return
						"Resumes execution of the selected process, but does not wait for it.\n\n" +
						"The difference to `continue' is that `continue' waits until the process\n" +
						"stops again (for instance, because it hit a breakpoint or received a signal)\n" +
						"while this command just lets the process running.  Note that the process\n" +
						"still stops if it hits a breakpoint.\n"; } }
	}

	public class StopProcessCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Stop ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Threads; } }
		public string Description { get { return "Stop execution of a process"; } }
		public string Documentation { get { return ""; } }
	}

	public class ContinueCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Continue);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Continue program being debugged."; } }
		public string Documentation { get { return ""; } }
	}

	public class StepCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Step);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Step program untli it reaches a different source line, proceeding into function calls."; } }
		public string Documentation { get { return ""; } }
	}

	public class NextCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Next);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Step program until it reaches a different source line, skipping over function calls."; } }
		public string Documentation { get { return ""; } }
	}

	public class StepInstructionCommand : ProcessCommand, CL.IDocumentableCommand
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Step program 1 instruction, but do not enter trampolines."; } }
		public string Documentation { get { return ""; } }
	}

	public class NextInstructionCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.NextInstruction);
			context.ResetCurrentSourceCode ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Next instruction"; } }
		public string Documentation { get { return "Steps one machine instruction, but steps over method calls."; } }
	}

	public class FinishCommand : ProcessCommand, CL.IDocumentableCommand
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Execute until selected stack frame returns."; } }
		public string Documentation { get { return ""; } }
	}

	public class BacktraceCommand : ProcessCommand, CL.IDocumentableCommand
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Stack; } }
		public string Description { get { return "Print backtrace of all stack frames."; } }
		public string Documentation { get { return ""; } }
	}

	public class UpCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex++;
			context.Interpreter.Style.PrintFrame (context, process.CurrentFrame);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Stack; } }
		public string Description { get { return "Select and print stack frame that called this one."; } }
		public string Documentation { get { return ""; } }
	}

	public class DownCommand : ProcessCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex--;
			context.Interpreter.Style.PrintFrame (context, process.CurrentFrame);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Stack; } }
		public string Description { get { return "Select and print stack frame called by this one."; } }
		public string Documentation { get { return ""; } }
	}

	public class RunCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		protected override bool NeedsProcess {
			get { return false; }
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Run ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Start debugged program."; } }
		public string Documentation { get { return ""; } }
	}

	public class KillCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Kill ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Kill the program being debugged."; } }
		public string Documentation { get { return ""; } }
	}

	public class QuitCommand : CL.Command, CL.IDocumentableCommand
	{
		public override string Execute (CL.Engine e)
		{
			DebuggerEngine engine = (DebuggerEngine) e;
			
			engine.Context.Interpreter.Exit ();
			return null;
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Support; } }
		public string Description { get { return "Exit mdb."; } }
		public string Documentation { get { return ""; } }
	}

	public class ShowCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		DebuggerCommand subcommand;

		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count < 1)) {
				context.Print ("Need an argument: processes registers " +
					       "locals parameters breakpoints modules " +
					       "sources methods");
				return false;
			}

			switch ((string) Args [0]) {
			case "processes":
			case "procs":
				subcommand = new ShowProcessesCommand ();
				break;
			case "registers":
			case "regs":
				subcommand = new ShowRegistersCommand ();
				break;
			case "locals":
				subcommand = new ShowLocalsCommand ();
				break;
			case "parameters":
			case "params":
				subcommand = new ShowParametersCommand ();
				break;
			case "breakpoints":
				subcommand = new ShowBreakpointsCommand ();
				break;
			case "modules":
				subcommand = new ShowModulesCommand ();
				break;
			case "threadgroups":
				subcommand = new ShowThreadGroupsCommand ();
				break;
			case "methods":
				subcommand = new ShowMethodsCommand ();
				break;
			case "sources":
				subcommand = new ShowSourcesCommand ();
				break;
			case "frame":
				subcommand = new ShowFrameCommand ();
				break;
			default:
				context.Error ("Syntax error");
				return false;
			}

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

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Support; } }
		public string Description { get { return "Show things."; } }
		public string Documentation { get { return "valid args are:\n" +
						"processes registers " +
						"locals parameters breakpoints modules " +
						"sources methods"; } }
	}

	public class ShowProcessesCommand : DebuggerCommand
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

	public class ShowRegistersCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			IArchitecture arch = ResolveProcess (context).Process.Architecture;
			context.Print (arch.PrintRegisters (frame.Frame));
		}
	}

	public class ShowParametersCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			frame.ShowParameters (context);
		}
	}

	public class ShowLocalsCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			frame.ShowLocals (context);
		}
	}

	public class ShowModulesCommand : DebuggerCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowModules ();
		}
	}

	public class ShowSourcesCommand : DebuggerCommand
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

		protected override void DoExecute (ScriptingContext context)
		{
			foreach (Module module in modules)
				context.Interpreter.ShowSources (module);
		}
	}

	public class ShowMethodsCommand : DebuggerCommand
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

		protected override void DoExecute (ScriptingContext context)
		{
			foreach (SourceFile source in sources)
				context.PrintMethods (source);
		}
	}

	public class ShowBreakpointsCommand : DebuggerCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowBreakpoints ();
		}
	}

	public class ShowThreadGroupsCommand : DebuggerCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowThreadGroups ();
		}
	}

	public class ShowFrameCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			StackFrame frame = ResolveFrame (context).Frame;

			context.Print ("Stack level {0}, stack pointer at {1}, " +
				       "frame pointer at {2}.", frame.Level,
				       frame.StackPointer, frame.FrameAddress);
		}
	}

	public class ThreadGroupCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		DebuggerCommand subcommand;

		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count < 1)) {
				context.Print ("Need an argument: create add remove delete");
				return false;
			}

			switch ((string) Args [0]) {
			case "create":
				subcommand = new ThreadGroupCreateCommand ();
				break;
			case "delete":
				subcommand = new ThreadGroupDeleteCommand ();
				break;
			case "add":
				subcommand = new ThreadGroupAddCommand ();
				break;
			case "remove":
				subcommand = new ThreadGroupRemoveCommand ();
				break;
			default:
				context.Error ("Syntax error");
				return false;
			}

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

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Threads; } }
		public string Description { get { return "Manage thread groups."; } }
		public string Documentation { get { return "valid args are: create add remove delete"; } }
	}

	public class ThreadGroupCreateCommand : DebuggerCommand
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

	public class ThreadGroupDeleteCommand : DebuggerCommand
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

	public class ThreadGroupAddCommand : DebuggerCommand
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

	public class ThreadGroupRemoveCommand : ThreadGroupAddCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.AddToThreadGroup (name, threads);
		}
	}

	public class BreakpointEnableCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		protected BreakpointHandle handle;

		protected override bool DoResolve (ScriptingContext context)
		{
			int id;
			try {
				id = (int) UInt32.Parse (Argument);
			} catch {
				context.Print ("Breakpoint number expected.");
				return false;
			}

			handle = context.Interpreter.GetBreakpoint (id);
			return handle != null;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			handle.EnableBreakpoint (context.CurrentProcess.Process);
		}

		// IDocumentableCommand
		public virtual CL.CommandFamily Family { get { return CL.CommandFamily.Breakpoints; } }
		public virtual string Description { get { return "Enable breakpoint."; } }
		public virtual string Documentation { get { return ""; } }
	}

	public class BreakpointDisableCommand : BreakpointEnableCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			handle.DisableBreakpoint (context.CurrentProcess.Process);
		}

		// IDocumentableCommand
		public override CL.CommandFamily Family { get { return CL.CommandFamily.Breakpoints; } }
		public override string Description { get { return "Disable breakpoint."; } }
		public override string Documentation { get { return ""; } }
	}

	public class BreakpointDeleteCommand : BreakpointEnableCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.DeleteBreakpoint (context.CurrentProcess, handle);
		}

		// IDocumentableCommand
		public override CL.CommandFamily Family { get { return CL.CommandFamily.Breakpoints; } }
		public override string Description { get { return "Delete breakpoint."; } }
		public override string Documentation { get { return ""; } }
	}

#if FIXME
	[ShortDescription("MODULE", "Change module parameters",
		 "The module parameters control how the debugger should behave while single-stepping\n" +
		 "wrt methods from this method.\n\n" +
		 "Use `show modules' to get a list of modules.\n" +
		 "Use `help module_operations' to get help about module operations.\n\n" +
		 "Example:  module 1,2 !ignore step\n")]
	public class ModuleOperationCommand : DebuggerCommand
	{
		ModuleListExpression module_list_expr;
		ModuleOperationListExpression op_list_expr;

		public ModuleOperationCommand (ModuleListExpression module_list_expr,
					       ModuleOperationListExpression op_list_expr)
		{
			this.module_list_expr = module_list_expr;
			this.op_list_expr = op_list_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Module[] modules = (Module []) module_list_expr.Resolve (context);
			ModuleOperation[] operations = (ModuleOperation []) op_list_expr.Resolve (context);

			context.Interpreter.ModuleOperations (modules, operations);
		}
	}
#endif

	public abstract class SourceCommand : DebuggerCommand
	{
		int method_id = -1;
		bool is_getter = false;
		bool is_setter = false;
		bool is_event_add = false;
		bool is_event_remove = false;
		protected SourceLocation location;

		public int ID {
			get { return method_id; }
			set { method_id = value; }
		}

		public bool Get {
			get { return is_getter; }
			set { is_getter = value; }
		}

		public bool Set {
			get { return is_setter; }
			set { is_setter = value; }
		}

		public bool Add {
			get { return is_event_add; }
			set { is_event_add = value; }
		}

		public bool Remove {
			get { return is_event_remove; }
			set { is_event_remove = value; }
		}

		protected bool DoResolveExpression (ScriptingContext context)
		{
			Expression expr = ParseExpression (context);
			if (expr == null)
				return false;

			expr = expr.Resolve (context);
			if (expr == null)
				return false;

			if (is_getter)
				location = expr.EvaluateLocation (context, LocationType.PropertyGetter, null);
			else if (is_setter)
				location = expr.EvaluateLocation (context, LocationType.PropertySetter, null);
			else if (is_event_add)
				location = expr.EvaluateLocation (context, LocationType.EventAdd, null);
			else if (is_event_remove)
				location = expr.EvaluateLocation (context, LocationType.EventRemove, null);
			else
				location = expr.EvaluateLocation (context, LocationType.Method, null);
			return location != null;
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (ID > 0) {
				if (Argument != "") {
					context.Error ("Cannot specify both a method id " +
						       "and an expression.");
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

				location = context.Interpreter.FindLocation (filename, line);
				return true;
			}

			if (Argument == "")
				return true;

			try {
				line = (int) UInt32.Parse (Argument);
			} catch {
				return DoResolveExpression (context);
			}

			location = context.Interpreter.FindLocation (
				context.CurrentLocation.SourceFile.FileName, line);
			return true;
		}
	}

	public class ListCommand : SourceCommand, CL.IDocumentableCommand
	{
		int lines = 10;

		public int Lines {
			get { return lines; }
			set { lines = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Repeating)
				return true;
			else
				return base.DoResolve (context);
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (Repeating)
				context.ListSourceCode (null, Lines);
			else
				context.ListSourceCode (location, Lines);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Files; } }
		public string Description { get { return "List source code."; } }
		public string Documentation { get { return ""; } }
	}

	public class BreakCommand : SourceCommand, CL.IDocumentableCommand
	{
		string group;
		int process_id = -1;
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

		protected override bool DoResolve (ScriptingContext context)
		{
			if (!base.DoResolve (context))
				return false;

			if (process_id > 0) {
				process = context.Interpreter.GetProcess (process_id);
				if (group == null)
					tgroup = process.ThreadGroup;
			} else
				process = context.CurrentProcess;

			if (tgroup == null)
				tgroup = context.Interpreter.GetThreadGroup (Group, false);

			if (location == null)
				location = context.CurrentLocation;

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			int index = context.Interpreter.InsertBreakpoint (
				process, tgroup, location);
			context.Print ("Inserted breakpoint {0} at {1}",
				       index, location.Name);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Breakpoints; } }
		public string Description { get { return "Insert breakpoint."; } }
		public string Documentation { get { return ""; } }
	}

	public class CatchCommand : FrameCommand, CL.IDocumentableCommand
	{
		string group;
		FrameHandle frame;
		ThreadGroup tgroup;
		ITargetClassType type;

		public string Group {
			get { return group; }
			set { group = value; }
		}

		bool IsSubclassOf (ITargetClassType type, ITargetType parent)
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

			ITargetType exception_type = frame.Language.ExceptionType;
			if (exception_type == null)
				throw new ScriptingException ("Current language doesn't have any exceptions.");

			Expression expr = ParseExpression (context);
			if (expr == null)
				return false;

			expr = expr.ResolveType (context);
			if (expr == null)
				return false;

			type = expr.EvaluateType (context) as ITargetClassType;
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Catchpoints; } }
		public string Description { get { return "Stop execution when an exception is raised."; } }
		public string Documentation { get { return
						"Argument should be a subclass of the target\n" +
						"language's exception type.  This command causes\n" +
						"execution of the program to stop at the point where\n" +
						"the exception is thrown, so you can examine locals\n" +
						"in that particular stack frame"; } }
	}

	public class DumpCommand : FrameCommand, CL.IDocumentableCommand
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Internal; } }
		public string Description { get { return "Dump detailed information about an expression."; } }
		public string Documentation { get { return ""; } }
	}

	public class LibraryCommand : FrameCommand, CL.IDocumentableCommand
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
			context.Interpreter.LoadLibrary (
				context.CurrentProcess.Process, Argument);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Files; } }
		public string Description { get { return "Load a library."; } }
		public string Documentation { get { return ""; } }
	}

	public class SaveCommand : DebuggerCommand, CL.IDocumentableCommand
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
			using (FileStream stream = new FileStream (Argument, FileMode.Create))
				context.Interpreter.SaveSession (stream);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Obscure; } }
		public string Description { get { return "Save a debugger session."; } }
		public string Documentation { get { return ""; } }
	}

	public class LoadCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		protected override bool NeedsProcess {
			get { return false; }
		}

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
			using (FileStream stream = new FileStream (Argument, FileMode.Open))
				context.Interpreter.LoadSession (stream);
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Obscure; } }
		public string Description { get { return "Load a debugger session."; } }
		public string Documentation { get { return ""; } }
	}

	public class RestartCommand : DebuggerCommand, CL.IDocumentableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Restart ();
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Running; } }
		public string Description { get { return "Restart the program being debugged."; } }
		public string Documentation { get { return ""; } }
	}

	public class HelpCommand : CL.Command, CL.IDocumentableCommand
	{
		public override string Execute (CL.Engine e)
		{
			if (Args == null || Args.Count == 0) {
				Console.WriteLine ("List of families of commands:\n");
				Console.WriteLine ("aliases -- Aliases of other commands");

				foreach (CL.CommandFamily family in Enum.GetValues(typeof (CL.CommandFamily)))
					Console.WriteLine ("{0} -- {1}", family.ToString().ToLower(), e.CommandFamilyBlurbs[(int)family]);

				Console.WriteLine ("\n" + 
						   "Type \"help\" followed by a class name for a list of commands in that family.\n" +
						   "Type \"help\" followed by a command name for full documentation.\n");
			}
			else {
				string[] args = (string []) Args.ToArray (typeof (string));
				int family_index = -1;
				int i;

				string[] family_names = Enum.GetNames (typeof (CL.CommandFamily));
				for (i = 0; i < family_names.Length; i ++) {
					if (family_names[i].ToLower() == args[0]) {
						family_index = i;
						break;
					}
				}

				if (family_index != -1) {
					ArrayList cmds = (ArrayList) e.CommandsByFamily [family_index];

					/* we're printing out a command family */
					Console.WriteLine ("List of commands:\n");
					foreach (string cmd_name in cmds) {
						Type cmd_type = (Type) e.Commands[cmd_name];
						CL.IDocumentableCommand c = (CL.IDocumentableCommand) Activator.CreateInstance (cmd_type);
						Console.WriteLine ("{0} -- {1}", cmd_name, c.Description);
					}

					Console.WriteLine ("\n" +
							   "Type \"help\" followed by a command name for full documentation.\n");
				}
				else if (e.Commands[args[0]] != null) {
					/* we're printing out a command */
					Type cmd_type = (Type) e.Commands[args[0]];
					if (cmd_type.GetInterface ("CL.IDocumentableCommand") != null) {
						CL.IDocumentableCommand c = (CL.IDocumentableCommand) Activator.CreateInstance (cmd_type);

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
						if (cmd_type.GetInterface ("CL.IDocumentableCommand") != null) {
							CL.IDocumentableCommand c = (CL.IDocumentableCommand) Activator.CreateInstance (cmd_type);

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

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Support; } }
		public string Description { get { return "Print list of commands."; } }
		public string Documentation { get { return ""; } }
	}

	public class AboutCommand : CL.Command, CL.IDocumentableCommand
	{
		public override string Execute (CL.Engine e)
		{
			Console.WriteLine ("Mono Debugger (C) 2003, 2004 Novell, Inc.\n" +
					   "Written by Martin Baulig (martin@ximian.com)\n" +
					   "        and Chris Toshok (toshok@ximian.com)\n");
			return null;
		}

		// IDocumentableCommand
		public CL.CommandFamily Family { get { return CL.CommandFamily.Support; } }
		public string Description { get { return "Print copyright/authour info."; } }
		public string Documentation { get { return ""; } }
	}

	public class LookupCommand : FrameCommand, CL.IDocumentableCommand
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

			TargetLocation location = pexpr.EvaluateAddress (context);
			if (location == null)
				throw new ScriptingException (
					"Cannot evaluate expression `{0}'", pexpr.Name);

			if (!location.HasAddress)
				throw new ScriptingException (
					"Cannot get address of expression `{0}'", pexpr.Name);

			TargetAddress address = location.GlobalAddress;
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
		public CL.CommandFamily Family { get { return CL.CommandFamily.Internal; } }
		public string Description { get { return "Print the method containing the address."; } }
		public string Documentation { get { return ""; } }
	}

	public class UnwindCommand : ProcessCommand, CL.IDocumentableCommand
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

			TargetLocation location = pexpr.EvaluateAddress (context);
			if (location == null)
				throw new ScriptingException (
					"Cannot evaluate expression `{0}'", pexpr.Name);

			if (!location.HasAddress)
				throw new ScriptingException (
					"Cannot get address of expression `{0}'", pexpr.Name);

			TargetAddress address = location.GlobalAddress;
			ProcessHandle process = new_context.CurrentProcess;

			Backtrace backtrace = process.Process.UnwindStack (address);
			StackFrame[] frames = backtrace.Frames;
			for (int i = 0; i < frames.Length; i++)
				context.Print ("{0} {1}", "   ", frames [i]);
		}

		  // IDocumentableCommand
		  public CL.CommandFamily Family { get { return CL.CommandFamily.Internal; } }
		  public string Description { get { return "Unwind the stack to a given address."; } }
		  public string Documentation { get { return ""; } }
	}

}
