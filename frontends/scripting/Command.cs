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

		public void Execute (ScriptingContext context)
		{
			try {
				DoExecute (context);
			} catch (ScriptingException ex) {
				context.Error (ex);
			}
		}
	}

	public abstract class TargetCommand : Command
	{
		ProcessExpression process_expr;
		FrameExpression frame_expr;

		[Argument(ArgumentType.Process, "proc", "Target process to operate on")]
		public ProcessExpression ProcessExpression {
			get { return process_expr; }
			set { process_expr = value; }
		}

		[Argument(ArgumentType.Frame, "frame", "Stack frame")]
		public FrameExpression FrameExpression {
			get { return frame_expr; }
			set { frame_expr = value; }
		}

		protected virtual ProcessHandle ResolveProcess (ScriptingContext context)
		{
			if (process_expr != null)
				return (ProcessHandle) process_expr.Resolve (context);

			return context.CurrentProcess;
		}

		protected FrameHandle ResolveFrame (ScriptingContext context)
		{
			if (frame_expr != null)
				return (FrameHandle) frame_expr.Resolve (context);

			return ResolveProcess (context).CurrentFrame;
		}
	}

	[Command("PRINT", "Print the result of an expression")]
	public class PrintCommand : Command
	{
		Expression expression;

		public PrintCommand (Expression expression)
		{
			this.expression = expression;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			object retval = expression.Resolve (context);
			context.PrintObject (retval);
		}
	}

	[Command("EXAMINE", "Examine memory.")]
	public class ExamineCommand : Command
	{
		VariableExpression expression;
		FrameExpression frame_expr;
		long address;
		Format format;

		public ExamineCommand (Format format, VariableExpression expression)
		{
			this.format = format;
			this.expression = expression;
		}

		public ExamineCommand (Format format, FrameExpression frame_expr, long address)
		{
			this.format = format;
			this.frame_expr = frame_expr;
			this.address = address;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			TargetAddress taddress;
			ITargetAccess taccess;
			byte[] data;

			RegisterExpression rexp = expression as RegisterExpression;
			if (rexp != null) {
				TargetLocation location = rexp.ResolveLocation (context);

				taddress = location.Address;
				taccess = location.TargetAccess;
			} else if (expression != null) {
				ITargetObject obj = expression.ResolveVariable (context);

				if (!obj.Location.HasAddress)
					throw new ScriptingException ("Expression doesn't have an address.");

				taddress = obj.Location.Address;
				taccess = obj.Location.TargetAccess;
			} else {
				FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

				taddress = new TargetAddress (frame.Frame.AddressDomain, address);
				taccess = frame.Frame.TargetAccess;
			}

			try {
				data = taccess.ReadBuffer (taddress, format.Size);
			} catch (TargetException) {
				throw new ScriptingException ("Can't access target memory at address {0}.", taddress);
			}

			context.Print (TargetBinaryReader.HexDump (taddress, data));
		}

		public struct Format
		{
			int size;

			public static readonly Format Standard = new Format (16);

			public Format (int size)
			{
				this.size = size;
			}

			public int Size {
				get {
					return size;
				}
			}
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
	public class FrameCommand : TargetCommand
	{
		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = ResolveFrame (context);

			frame.Print (context);
		}
	}

	[Command("DISASSEMBLE INSTRUCTION", "Disassemble current instruction")]
	public class DisassembleCommand : Command
	{
		FrameExpression frame_expr;

		public DisassembleCommand (FrameExpression frame_expr)
		{
			this.frame_expr = frame_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			frame.Disassemble (context);
		}
	}

	[Command("DISASSEMBLE METHOD", "Disassemble current method")]
	public class DisassembleMethodCommand : Command
	{
		FrameExpression frame_expr;

		public DisassembleMethodCommand (FrameExpression frame_expr)
		{
			this.frame_expr = frame_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			frame.DisassembleMethod (context);
		}
	}

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

	[Command("RUN", "Restart current program",
		 "After the program being debugged exited, run it a second time.\n\n")]
	public class RunCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			if (!context.IsSynchronous)
				throw new ScriptingException ("This command cannot be used in the GUI.");

			context.Interpreter.Initialize ();
			try {
				context.Interpreter.Run ();
			} catch (TargetException e) {
				throw new ScriptingException (e.Message);
			}
		}
	}

	[Command("process", "Select current process",
		 "Without argument, print the current process.\n\n" +
		 "With a process argument, make that process the current process.\n" +
		 "This is the process which is used if you do not explicitly specify\n" +
		 "a process (see `help process_expression' for details).\n")]
	public class SelectProcessCommand : Command
	{
		ProcessExpression process_expr;

		public SelectProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			context.CurrentProcess = process;
		}
	}

	[Command("BACKGROUND", "Run process in background",
		 "Resumes execution of the selected process, but does not wait for it.\n\n" +
		 "The difference to `continue' is that `continue' waits until the process\n" +
		 "stops again (for instance, because it hit a breakpoint or received a signal)\n" +
		 "while this command just lets the process running.  Note that the process\n" +
		 "still stops if it hits a breakpoint.\n")]
	public class BackgroundProcessCommand : Command
	{
		ProcessExpression process_expr;

		public BackgroundProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Background ();
		}
	}

	[Command("STOP", "Stop execution of a process")]
	public class StopProcessCommand : Command
	{
		ProcessExpression process_expr;

		public StopProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Stop ();
		}
	}

	[Command("CONTINUE", "Continue execution of the target")]
	public class ContinueCommand : Command
	{
		ProcessExpression process_expr;

		public ContinueCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (WhichStepCommand.Continue);
		}
	}

	[Command("STEP", "Step one source line")]
	public class StepCommand : Command
	{
		ProcessExpression process_expr;

		public StepCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (WhichStepCommand.Step);
		}
	}

	[Command("NEXT", "Next source line",
		 "Steps one source line, but does not enter any methods.")]
	public class NextCommand : Command
	{
		ProcessExpression process_expr;

		public NextCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (WhichStepCommand.Next);
		}
	}

	[Command("STEPI", "Step one instruction, but don't enter trampolines")]
	public class StepInstructionCommand : Command
	{
		ProcessExpression process_expr;

		public StepInstructionCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (WhichStepCommand.StepInstruction);
		}
	}

	[Command("NATIVE STEPI", "Step one instruction")]
	public class StepNativeInstructionCommand : Command
	{
		ProcessExpression process_expr;

		public StepNativeInstructionCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (WhichStepCommand.StepNativeInstruction);
		}
	}

	[Command("NEXTI", "Next instruction",
		 "Steps one machine instruction, but steps over method calls.")]
	public class NextInstructionCommand : Command
	{
		ProcessExpression process_expr;

		public NextInstructionCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (WhichStepCommand.NextInstruction);
		}
	}

	[Command("FINISH", "Run until exit of current method")]
	public class FinishCommand : Command
	{
		ProcessExpression process_expr;

		public FinishCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (WhichStepCommand.Finish);
		}
	}

	[Command("BACKTRACE", "Print backtrace")]
	public class BacktraceCommand : Command
	{
		ProcessExpression process_expr;
		int max_frames;

		public BacktraceCommand (ProcessExpression process_expr, int max_frames)
		{
			this.process_expr = process_expr;
			this.max_frames = max_frames;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			int current_idx = process.CurrentFrameIndex;
			BacktraceHandle backtrace = process.GetBacktrace (max_frames);

			for (int i = 0; i < backtrace.Length; i++) {
				string prefix = i == current_idx ? "(*)" : "   ";
				context.Print ("{0} {1}", prefix, backtrace [i]);
			}
		}
	}

	[Command("UP", "Go one frame up")]
	public class UpCommand : Command
	{
		ProcessExpression process_expr;

		public UpCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.CurrentFrameIndex++;
			process.CurrentFrame.Print (context);
		}
	}

	[Command("DOWN", "Go one frame down")]
	public class DownCommand : Command
	{
		ProcessExpression process_expr;

		public DownCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.CurrentFrameIndex--;
			process.CurrentFrame.Print (context);
		}
	}

	[Command("KILL", "Kill the target")]
	public class KillCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.Kill ();
		}
	}

	[Command("SHOW PROCESSES", "Show processes")]
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

	[Command("SHOW REGISTERS", "Show registers")]
	public class ShowRegistersCommand : Command
	{
		FrameExpression frame_expr;

		public ShowRegistersCommand (FrameExpression frame_expr)
		{
			this.frame_expr = frame_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			string[] names = frame.RegisterNames;
			int[] indices = frame.RegisterIndices;

			foreach (Register register in frame.GetRegisters (indices))
				context.Print ("%{0} = 0x{1:x}", names [register.Index], register.Data);
		}
	}

	[Command("SHOW PARAMETERS", "Show method parameters")]
	public class ShowParametersCommand : Command
	{
		FrameExpression frame_expr;

		public ShowParametersCommand (FrameExpression frame_expr)
		{
			this.frame_expr = frame_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			frame.ShowParameters (context);
		}
	}

	[Command("SHOW LOCALS", "Show local variables")]
	public class ShowLocalsCommand : Command
	{
		FrameExpression frame_expr;

		public ShowLocalsCommand (FrameExpression frame_expr)
		{
			this.frame_expr = frame_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

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

	[Command("SHOW MODULES", "Show modules")]
	public class ShowModulesCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowModules ();
		}
	}

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

	[Command("SHOW BREAKPOINTS", "Show breakpoints")]
	public class ShowBreakpointsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.Interpreter.ShowBreakpoints ();
		}
	}

	[Command("SHOW THREADGROUPS", "Show thread groups")]
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
}
