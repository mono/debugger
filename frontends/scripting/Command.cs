using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontends.CommandLine
{
	public abstract class Command
	{
		protected abstract void DoExecute (ScriptingContext context);

		protected virtual bool DoResolveBase (ScriptingContext context)
		{
			return true;
		}

		protected virtual bool DoResolve (ScriptingContext context, object[] args)
		{
			if (args != null) {
				context.Error ("This command doesn't take any arguments");
				return false;
			}

			return DoResolveBase (context);
		}

		public bool Resolve (ScriptingContext context, object[] arguments)
		{
			try {
				return DoResolve (context, arguments);
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
			}
		}
	}

	public abstract class ProcessCommand : Command
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

		protected override bool DoResolve (ScriptingContext context, object[] args)
		{
			if ((args == null) || (args.Length != 1)) {
				context.Error ("`print' takes exactly one argument");
				return false;
			}

			expression = args [0] as Expression;
			if (expression == null) {
				context.Print ("Argument is not an expression");
				return false;
			}

			return true;
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

		protected override bool DoResolve (ScriptingContext context, object[] args)
		{
			if ((args == null) || (args.Length != 1)) {
				context.Error ("`ptype' takes exactly one argument");
				return false;
			}

			expression = args [0] as Expression;
			if (expression == null) {
				context.Print ("Argument is not an expression");
				return false;
			}

			return true;
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
	public class ExamineCommand : Command
	{
		Expression expression;
		int size = 16;

		public int Size {
			get { return size; }
			set { size = value; }
		}

		protected override bool DoResolve (ScriptingContext context, object[] args)
		{
			if ((args == null) || (args.Length != 1)) {
				context.Error ("`examine' takes exactly one argument");
				return false;
			}

			expression = args [0] as Expression;
			if (expression == null) {
				context.Print ("Argument is not an expression");
				return false;
			}

			return true;
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
	public class CallMethodCommand : Command
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
	public class StartCommand : Command
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
	public class KillCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Kill ();
		}
	}

	[Command("show processes", "Show processes")]
	public class ShowProcessesCommand : Command
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

	[Command("show registers", "Show registers")]
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

	[Command("show parameters", "Show method parameters")]
	public class ShowParametersCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			frame.ShowParameters (context);
		}
	}

	[Command("show locals", "Show local variables")]
	public class ShowLocalsCommand : FrameCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			frame.ShowLocals (context);
		}
	}

	[Command("SHOW TYPE", "Show type of variable")]
	public class ShowVariableTypeCommand : Command
	{
		VariableExpression var_expr;

		public ShowVariableTypeCommand (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ITargetType type = var_expr.ResolveType (context);

			context.Interpreter.ShowVariableType (type, var_expr.Name);
		}
	}

	[Command("show modules", "Show modules")]
	public class ShowModulesCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowModules ();
		}
	}

#if FIXME
	[Command("SHOW SOURCES", "Show source files")]
	public class ShowSourcesCommand : Command
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
	public class ShowMethodsCommand : Command
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

	[Command("show breakpoints", "Show breakpoints")]
	public class ShowBreakpointsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowBreakpoints ();
		}
	}

	[Command("show threadgroups", "Show thread groups")]
	public class ShowThreadGroupsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowThreadGroups ();
		}
	}

	[Command("THREADGROUP CREATE", "Create a new thread group")]
	public class ThreadGroupCreateCommand : Command
	{
		string name;

		public ThreadGroupCreateCommand (string name)
		{
			this.name = name;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.CreateThreadGroup (name);
		}
	}

	[Command("THREADGROUP ADD", "Add threads to a thread group")]
	public class ThreadGroupAddCommand : Command
	{
		string name;
		ProcessListExpression process_list_expr;

		public ThreadGroupAddCommand (string name, ProcessListExpression process_list_expr)
		{
			this.name = name;
			this.process_list_expr = process_list_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle[] threads = (ProcessHandle []) process_list_expr.Resolve (context);

			context.Interpreter.AddToThreadGroup (name, threads);
		}
	}

	[Command("THREADGROUP REMOVE", "Remove threads from a thread group")]
	public class ThreadGroupRemoveCommand : Command
	{
		string name;
		ProcessListExpression process_list_expr;

		public ThreadGroupRemoveCommand (string name, ProcessListExpression process_list_expr)
		{
			this.name = name;
			this.process_list_expr = process_list_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle[] threads = (ProcessHandle []) process_list_expr.Resolve (context);

			context.Interpreter.RemoveFromThreadGroup (name, threads);
		}
	}

	[Command("BREAKPOINT ENABLE", "Enable breakpoint")]
	public class BreakpointEnableCommand : Command
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
	public class BreakpointDisableCommand : Command
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
	public class BreakpointDeleteCommand : Command
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
	public class ModuleOperationCommand : Command
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
	public class BreakCommand : Command
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

	public class ScriptingVariableAssignCommand : Command
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
	public class ListCommand : Command
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

	public class VariableAssignCommand : Command
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
