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

	[ShortDescription("Print the result of an expression")]
	public class PrintExpressionCommand : PrintCommand
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
	}

	[ShortDescription("Print the type of an expression")]
	public class PrintTypeCommand : PrintCommand
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
	}

	[ShortDescription("Invokes a method")]
	public class CallCommand : FrameCommand
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
	}

	[ShortDescription("Selects the output stype")]
	public class StyleCommand : DebuggerCommand
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
	}

	[ShortDescription("Examine memory.")]
	public class ExamineCommand : DebuggerCommand
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
	}

	[ShortDescription("Print the current stack frame.")]
	public class PrintFrameCommand : ProcessCommand
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
	}

	[ShortDescription("Disassemble current instruction or method")]
	public class DisassembleCommand : FrameCommand
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
	}

	public class StartCommand : DebuggerCommand
	{
		protected override bool NeedsProcess {
			get { return false; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count != 1)) {
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
				options.ProcessArgs (args);

				context.Interpreter.Start (options);
				context.Interpreter.Initialize ();
				context.Interpreter.Run ();
			} catch (TargetException e) {
				context.Interpreter.Kill ();
				throw new ScriptingException (e.Message);
			}
		}
	}

	[ShortDescription("Print or select current process")]
	[Help("Without argument, print the current process.\n\n" +
	      "With a process argument, make that process the current process.\n" +
	      "This is the process which is used if you do not explicitly specify\n" +
	      "a process (see `help process_expression' for details).\n")]
	public class SelectProcessCommand : DebuggerCommand
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
	}

	[ShortDescription("Run process in background")]
	[Help("Resumes execution of the selected process, but does not wait for it.\n\n" +
	      "The difference to `continue' is that `continue' waits until the process\n" +
	      "stops again (for instance, because it hit a breakpoint or received a signal)\n" +
	      "while this command just lets the process running.  Note that the process\n" +
	      "still stops if it hits a breakpoint.\n")]
	public class BackgroundProcessCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Background ();
		}
	}

	[ShortDescription("Stop execution of a process")]
	public class StopProcessCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Stop ();
		}
	}

	[ShortDescription("Continue execution of the target")]
	public class ContinueCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Continue);
			context.ResetCurrentSourceCode ();
		}
	}

	[ShortDescription("Step one source line")]
	public class StepCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Step);
			context.ResetCurrentSourceCode ();
		}
	}

	[ShortDescription("Next source line")]
	[Help("Steps one source line, but does not enter any methods.")]
	public class NextCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Next);
			context.ResetCurrentSourceCode ();
		}
	}

	[ShortDescription("Step one instruction, but don't enter trampolines")]
	public class StepInstructionCommand : ProcessCommand
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
	}

	[ShortDescription("Next instruction")]
	[Help("Steps one machine instruction, but steps over method calls.")]
	public class NextInstructionCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.NextInstruction);
			context.ResetCurrentSourceCode ();
		}
	}

	[ShortDescription("Run until exit of current method")]
	public class FinishCommand : ProcessCommand
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
	}

	[ShortDescription("Print backtrace")]
	public class BacktraceCommand : ProcessCommand
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
	}

	[ShortDescription("Go one frame up")]
	public class UpCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex++;
			context.Interpreter.Style.PrintFrame (context, process.CurrentFrame);
		}
	}

	[ShortDescription("Go one frame down")]
	public class DownCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex--;
			context.Interpreter.Style.PrintFrame (context, process.CurrentFrame);
		}
	}

	[ShortDescription("Run the target")]
	public class RunCommand : DebuggerCommand
	{
		protected override bool NeedsProcess {
			get {
				return false;
			}
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Run ();
		}
	}

	[ShortDescription("Kill the target")]
	public class KillCommand : DebuggerCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Kill ();
		}
	}

	[ShortDescription("Quit the debugger")]
	public class QuitCommand : CL.Command
	{
		public override string Execute (CL.Engine e)
		{
			DebuggerEngine engine = (DebuggerEngine) e;
			
			engine.Context.Interpreter.Exit ();
			return null;
		}
	}

	[ShortDescription("Show things")]
	public class ShowCommand : DebuggerCommand
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

	[ShortDescription("Manage thread groups")]
	public class ThreadGroupCommand : DebuggerCommand
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

	[ShortDescription("Enable breakpoint")]
	public class BreakpointEnableCommand : DebuggerCommand
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
	}

	[ShortDescription("Disable breakpoint")]
	public class BreakpointDisableCommand : BreakpointEnableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			handle.DisableBreakpoint (context.CurrentProcess.Process);
		}
	}

	[ShortDescription("Delete breakpoint")]
	public class BreakpointDeleteCommand : BreakpointEnableCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.DeleteBreakpoint (context.CurrentProcess, handle);
		}
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

	[ShortDescription("List source code")]
	public class ListCommand : SourceCommand
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
	}

	[ShortDescription("Insert breakpoint")]
	public class BreakCommand : SourceCommand
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
	}

	[ShortDescription("Catch exceptions")]
	public class CatchCommand : FrameCommand
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
	}

	public class DumpCommand : FrameCommand
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
	}

	public class LibraryCommand : FrameCommand
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
	}

	public class SaveCommand : DebuggerCommand
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
	}

	public class LoadCommand : DebuggerCommand
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
	}

	public class RestartCommand : DebuggerCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Restart ();
		}
	}

	public class HelpCommand : CL.Command {
		public override string Execute (CL.Engine e)
		{
			if (Args == null || Args.Count == 0){
				Console.WriteLine ("Topics:\n" +
				   "Examining data:\n" + 
				   "   print/p [expr]  prints value of expr   ptype [expr]    prints type of expr\n" +
				   "   examine/x expr  Examines memory\n" +
				   "\n" +
				   "Stack:\n" + 
				   "   frame           Shows current frame    backtrace/bt    Shows stack trace\n" + 
				   "   up              Goes up one frame      down            Goes down one frame\n" +
				   "\n" +
				   "Examining code:\n" + 
				   "   list source     Display source code    dis address     Disassemble at addr\n" + 
				   "\n" + 
				   "Breakpoints:\n" + 
				   "   b [-id n] expr  Sets a breakpoint      delete N        Deletes a breakpoint\n" +
				   "   disable N       disables breakpoint    enable N        Enables breakpoint N\n" +
				   "\n" +
				   "Execution:\n" +
				   "   run/r           Starts execution\n" + 
				   "   continue/c      Continues execution\n" + 
				   "   step/s          Single-step execution  next/n          Step-over execution\n" +
				   "   stepi/i         Machine single step    nexti/t         Machine step-over\n" + 
				   "   kill            Kills the process\n");
			}
			return null;
		}
	}

	public class AboutCommand : CL.Command {
		public override string Execute (CL.Engine e)
		{
			Console.WriteLine ("Mono Debugger (C) 2003, 2004 Novell, Inc.\n" +
					   "Written by Martin Baulig (martin@ximian.com)\n" +
					   "        and Chris Toshok (toshok@ximian.com)\n");
			return null;
		}
	}

	public class LookupCommand : FrameCommand
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
	}

	// Private command
	public class UnwindCommand : ProcessCommand
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
	}

}
