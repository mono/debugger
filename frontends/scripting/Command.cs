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

			ExpressionParser parser = new ExpressionParser (context, ToString ());

			Expression expr = parser.Parse (Argument);
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

	[ShortDescription("Print the result of an expression")]
	public class PrintCommand : FrameCommand
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

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			ResolveFrame (new_context);
			new_context.CurrentFrameIndex = Frame;

			object retval = expression.Evaluate (new_context);
			new_context.PrintObject (retval);
		}
	}

	[ShortDescription("Print the type of an expression")]
	public class PrintTypeCommand : FrameCommand
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

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			ResolveFrame (new_context);
			new_context.CurrentFrameIndex = Frame;

			ITargetType type = expression.EvaluateType (new_context);
			new_context.PrintType (type);
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
		Expression expression;
		int size = 16;

		public int Size {
			get { return size; }
			set { size = value; }
		}

		protected override bool DoResolve (ScriptingContext context)
		{
			if (Repeating) {
				if (context.LastExamineAddress.IsNull)
					throw new ScriptingException (
						"Don't have a last examine command " +
						"which I could continue.");
				expression = new PointerDereferenceExpression (
					new NumberExpression (
						context.LastExamineAddress.Address));
			} else
				expression = ParseExpression (context);

			if (expression == null)
				return false;

			expression = expression.Resolve (context);
			return expression != null;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			TargetAddress taddress;
			ITargetAccess taccess;
			byte[] data;

			PointerExpression pexp = expression as PointerExpression;
			if (pexp == null)
				throw new ScriptingException (
					"Expression `{0}' is not a pointer.", expression.Name);

			TargetLocation location = pexp.EvaluateAddress (context);

			taddress = location.Address;
			taccess = location.TargetAccess;

			data = taccess.ReadBuffer (taddress, size);
			context.Print (TargetBinaryReader.HexDump (taddress, data));
			context.LastExamineAddress = taddress + size;
		}
	}

	[ShortDescription("Print the current stack frame.")]
	public class PrintFrameCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			if (context.Interpreter.IsScript)
				context.Print (frame);
			else
				context.Interpreter.Style.PrintFrame (context, frame);
		}
	}

	[ShortDescription("Disassemble current instruction or method")]
	public class DisassembleCommand : FrameCommand
	{
		bool method;

		public bool Method {
			get { return method; }
			set { method = value; }
		}
	
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			if (method)
				frame.DisassembleMethod (context);
			else
				frame.Disassemble (context);
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
	public class SelectProcessCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
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
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
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
		protected SourceLocation location;

		public int ID {
			get { return method_id; }
			set { method_id = value; }
		}

		protected bool DoResolveExpression (ScriptingContext context)
		{
			Expression expr = ParseExpression (context);
			if (expr == null)
				return false;

			expr = expr.Resolve (context);
			if (expr == null)
				return false;

			location = expr.EvaluateLocation (context, null);
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
		int count = 10;

		public int Count {
			get { return count; }
			set { count = value; }
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
				context.ListSourceCode (null, Count);
			else
				context.ListSourceCode (location, Count);
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
					   "Written by Martin Baulig (martin@ximian.com)\n");
			return null;
		}
	}
}
