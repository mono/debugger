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
		public readonly ScriptingContext Context;

		public DebuggerEngine (ScriptingContext context)
		{
			this.Context = context;
		}
	}

	public abstract class DebuggerCommand : CL.Command
	{
		public override string Execute (CL.Engine e)
		{
			DebuggerEngine engine = (DebuggerEngine) e;
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
		int frame;

		public int Frame {
			get { return frame; }
			set { frame = value; }
		}

		protected virtual FrameHandle ResolveFrame (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			if (frame > 0)
				return process.GetFrame (frame);
			else
				return process.CurrentFrame;
		}
	}

	[Command("print", "Print the result of an expression")]
	public class PrintCommand : FrameCommand
	{
		Expression expression;

		protected override bool DoResolve (ScriptingContext context)
		{
			expression = ParseExpression (context);
			return expression != null;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			if (Frame > 0)
				new_context.CurrentFrame = ResolveFrame (new_context);

			object retval = expression.Resolve (new_context);
			new_context.PrintObject (retval);
		}
	}

	[Command("ptype", "Print the type of an expression")]
	public class PrintTypeCommand : FrameCommand
	{
		Expression expression;

		protected override bool DoResolve (ScriptingContext context)
		{
			expression = ParseExpression (context);
			return expression != null;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ScriptingContext new_context = context.GetExpressionContext ();
			if (Process > 0)
				new_context.CurrentProcess = ResolveProcess (new_context);
			if (Frame > 0)
				new_context.CurrentFrame = ResolveFrame (new_context);

			Expression type_expr = new TypeOfExpression (expression);
			ITargetType type = type_expr.ResolveType (new_context);
			new_context.Print (type);
		}
	}

	[Command("examine", "Examine memory.")]
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
			expression = ParseExpression (context);
			return expression != null;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			TargetAddress taddress;
			ITargetAccess taccess;
			byte[] data;

			PointerExpression pexp = expression as PointerExpression;
			if (pexp != null) {
				TargetLocation location = pexp.ResolveLocation (context);

				taddress = location.Address;
				taccess = location.TargetAccess;
			} else {
				ITargetObject obj = expression.ResolveVariable (context);

				if (!obj.Location.HasAddress)
					throw new ScriptingException (
						"Expression doesn't have an address.");

				taddress = obj.Location.Address;
				taccess = obj.Location.TargetAccess;
			}

			data = taccess.ReadBuffer (taddress, size);
			context.Print (TargetBinaryReader.HexDump (taddress, data));
		}
	}

	[Command("CALL", "Call a function in the target")]
	public class CallMethodCommand : DebuggerCommand
	{
		VariableExpression expression;
		Expression[] arguments;

		public CallMethodCommand (VariableExpression expression, Expression[] arguments)
		{
			this.expression = expression;
			this.arguments = arguments;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			InvocationExpression invocation = new InvocationExpression (expression, arguments);

			invocation.Invoke (context, false);
		}
	}

	[Command("frame", "Print the current stack frame.")]
	public class PrintFrameCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			frame.Print (context);
		}
	}

	[Command("disassemble", "Disassemble current instruction or method")]
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

#if FIXME
	public class StartCommand : DebuggerCommand
	{
		ProgramArgumentsExpression program_args_expr;

		public StartCommand (ProgramArgumentsExpression program_args_expr)
		{
			this.program_args_expr = program_args_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (!context.IsSynchronous)
				throw new ScriptingException ("This command cannot be used in the GUI.");

			string[] args = (string []) program_args_expr.Resolve (context);
			DebuggerOptions options = new DebuggerOptions ();
			context.Interpreter.Start (options, args);
			context.Interpreter.Initialize ();
			try {
				context.Interpreter.Run ();
			} catch (TargetException e) {
				throw new ScriptingException (e.Message);
			}
		}
	}
#endif

	[Command("process", "Print or select current process",
		 "Without argument, print the current process.\n\n" +
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

	[Command("background", "Run process in background",
		 "Resumes execution of the selected process, but does not wait for it.\n\n" +
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

	[Command("stop", "Stop execution of a process")]
	public class StopProcessCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Stop ();
		}
	}

	[Command("continue", "Continue execution of the target")]
	public class ContinueCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Continue);
		}
	}

	[Command("step", "Step one source line")]
	public class StepCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Step);
		}
	}

	[Command("next", "Next source line",
		 "Steps one source line, but does not enter any methods.")]
	public class NextCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Next);
		}
	}

	[Command("stepi", "Step one instruction, but don't enter trampolines")]
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
		}
	}

	[Command("nexti", "Next instruction",
		 "Steps one machine instruction, but steps over method calls.")]
	public class NextInstructionCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.NextInstruction);
		}
	}

	[Command("finish", "Run until exit of current method")]
	public class FinishCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);
			process.Step (WhichStepCommand.Finish);
		}
	}

	[Command("backtrace", "Print backtrace")]
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

			for (int i = 0; i < backtrace.Length; i++) {
				string prefix = i == current_idx ? "(*)" : "   ";
				context.Print ("{0} {1}", prefix, backtrace [i]);
			}
		}
	}

	[Command("up", "Go one frame up")]
	public class UpCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex++;
			process.CurrentFrame.Print (context);
		}
	}

	[Command("down", "Go one frame down")]
	public class DownCommand : ProcessCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = ResolveProcess (context);

			process.CurrentFrameIndex--;
			process.CurrentFrame.Print (context);
		}
	}

	[Command("kill", "Kill the target")]
	public class KillCommand : DebuggerCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Kill ();
		}
	}

	[Command("show", "Show things")]
	public class ShowCommand : DebuggerCommand
	{
		DebuggerCommand subcommand;

		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count < 1)) {
				context.Print ("Need an argument: processes registers " +
					       "locals parameters breakpoints modules");
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

			string[] names = frame.RegisterNames;
			int[] indices = frame.RegisterIndices;

			foreach (Register register in frame.GetRegisters (indices))
				context.Print ("%{0} = 0x{1:x}", names [register.Index], register.Data);
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

#if FIXME
	[Command("SHOW SOURCES", "Show source files")]
	public class ShowSourcesCommand : DebuggerCommand
	{
		ModuleListExpression module_list_expr;

		public ShowSourcesCommand (ModuleListExpression module_list_expr)
		{
			this.module_list_expr = module_list_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Module[] modules = (Module []) module_list_expr.Resolve (context);

			foreach (Module module in modules)
				context.Interpreter.ShowSources (module);
		}
	}

	[Command("SHOW METHODS", "Show methods")]
	public class ShowMethodsCommand : DebuggerCommand
	{
		SourceListExpression source_list_expr;

		public ShowMethodsCommand (SourceListExpression source_list_expr)
		{
			this.source_list_expr = source_list_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			SourceFile[] sources = (SourceFile []) source_list_expr.Resolve (context);

			foreach (SourceFile source in sources)
				context.Interpreter.ShowMethods (source);
		}
	}
#endif

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

	[Command("threadgroup", "Manage thread groups")]
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
		protected int[] procs;

		protected override bool DoResolve (ScriptingContext context)
		{
			if ((Args == null) || (Args.Count < 2)) {
				context.Print ("Invalid arguments: Need the name of the " +
					       "thread group to operate on and one ore more " +
					       "processes");
				return false;
			}

			name = (string) Args [0];
			procs = new int [Args.Count - 1];
			for (int i = 0; i < Args.Count - 1; i++) {
				try {
					procs [i] = (int) UInt32.Parse ((string) Args [i+1]);
				} catch {
					context.Print ("Invalid argument {0}: expected " +
						       "process id", i+1);
					return false;
				}
			}

			return true;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle[] threads = context.Interpreter.GetProcesses (procs);

			context.Interpreter.AddToThreadGroup (name, threads);
		}
	}

	public class ThreadGroupRemoveCommand : ThreadGroupAddCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle[] threads = context.Interpreter.GetProcesses (procs);

			context.Interpreter.AddToThreadGroup (name, threads);
		}
	}

#if FIXME
	[Command("BREAKPOINT ENABLE", "Enable breakpoint")]
	public class BreakpointEnableCommand : DebuggerCommand
	{
		ProcessExpression process_expr;
		BreakpointNumberExpression breakpoint_number_expr;

		public BreakpointEnableCommand (ProcessExpression process_expr, BreakpointNumberExpression breakpoint_number_expr)
		{
			this.process_expr = process_expr;
			this.breakpoint_number_expr = breakpoint_number_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			BreakpointHandle handle = (BreakpointHandle) breakpoint_number_expr.Resolve (context);
			handle.EnableBreakpoint (process.Process);
		}
	}

	[Command("BREAKPOINT DISABLE", "Disable breakpoint")]
	public class BreakpointDisableCommand : DebuggerCommand
	{
		ProcessExpression process_expr;
		BreakpointNumberExpression breakpoint_number_expr;

		public BreakpointDisableCommand (ProcessExpression process_expr, BreakpointNumberExpression breakpoint_number_expr)
		{
			this.process_expr = process_expr;
			this.breakpoint_number_expr = breakpoint_number_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			BreakpointHandle handle = (BreakpointHandle) breakpoint_number_expr.Resolve (context);
			handle.DisableBreakpoint (process.Process);
		}
	}

	[Command("BREAKPOINT DELETE", "Delete breakpoint")]
	public class BreakpointDeleteCommand : DebuggerCommand
	{
		ProcessExpression process_expr;
		BreakpointNumberExpression breakpoint_number_expr;

		public BreakpointDeleteCommand (ProcessExpression process_expr, BreakpointNumberExpression breakpoint_number_expr)
		{
			this.process_expr = process_expr;
			this.breakpoint_number_expr = breakpoint_number_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			BreakpointHandle handle = (BreakpointHandle) breakpoint_number_expr.Resolve (context);
			context.Interpreter.DeleteBreakpoint (process, handle);
		}
	}

	[Command("MODULE", "Change module parameters",
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

	[Command("BREAK", "Insert breakpoint")]
	public class BreakCommand : DebuggerCommand
	{
		ThreadGroupExpression thread_group_expr;
		SourceExpression source_expr;

		public BreakCommand (ThreadGroupExpression thread_group_expr,
				     SourceExpression source_expr)
		{
			this.thread_group_expr = thread_group_expr;
			this.source_expr = source_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ThreadGroup group = (ThreadGroup) thread_group_expr.Resolve (context);

			SourceLocation location = source_expr.ResolveLocation (context);
			if (location == null)
				return;

			int index = context.Interpreter.InsertBreakpoint (
				context.CurrentProcess, group, location);
			context.Print ("Inserted breakpoint {0}.", index);
		}
	}

	public class ScriptingVariableAssignCommand : DebuggerCommand
	{
		string identifier;
		VariableExpression expr;

		public ScriptingVariableAssignCommand (string identifier, VariableExpression expr)
		{
			this.identifier = identifier;
			this.expr = expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context [identifier] = expr;
		}
	}

	[Command("LIST", "List source code")]
	public class ListCommand : DebuggerCommand
	{
		SourceExpression source_expr;

		public ListCommand (SourceExpression source_expr)
		{
			this.source_expr = source_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (source_expr == null) {
				context.ListSourceCode (null);
				return;
			}

			SourceLocation location = source_expr.ResolveLocation (context);
			if (location == null)
				return;

			context.ListSourceCode (location);
		}
	}

	public class VariableAssignCommand : DebuggerCommand
	{
		VariableExpression var_expr;
		Expression expr;

		public VariableAssignCommand (VariableExpression var_expr, Expression expr)
		{
			this.var_expr = var_expr;
			this.expr = expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			object obj = expr.Resolve (context);
			var_expr.Assign (context, obj);
		}
	}
#endif
}
