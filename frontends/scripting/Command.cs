using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

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

			if (retval is long)
				context.Print (String.Format ("0x{0:x}", (long) retval));
			else
				context.Print (retval);
		}
	}

	[Command("FRAME", "Print the current stack frame.")]
	public class FrameCommand : Command
	{
		FrameExpression frame_expr;

		public FrameCommand (FrameExpression frame_expr)
		{
			this.frame_expr = frame_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

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
			context.Start (args);
		}
	}

	[Command("PROCESS", "Select current process")]
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

	[Command("BACKGROUND", "Run process in background")]
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

	[Command("STEPI", "Step one instruction")]
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

		public BacktraceCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			int current_idx = process.CurrentFrameIndex;
			BacktraceHandle backtrace = process.GetBacktrace ();
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

	[Command("KILL", "Kill a process")]
	public class KillProcessCommand : Command
	{
		ProcessExpression process_expr;

		public KillProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.Kill ();
		}
	}

	[Command("SHOW PROCESSES", "Show processes")]
	public class ShowProcessesCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			if (!context.HasTarget) {
				context.Print ("No target.");
				return;
			}

			int current_id = context.CurrentProcess.ID;
			foreach (ProcessHandle proc in context.Processes) {
				string prefix = proc.ID == current_id ? "(*)" : "   ";
				context.Print ("{0} {1}", prefix, proc);
			}
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

			context.ShowVariableType (type, var_expr.Name);
		}
	}

	[Command("SHOW MODULES", "Show modules")]
	public class ShowModulesCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowModules ();
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
				context.ShowSources (module);
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
				context.ShowMethods (source);
		}
	}

	[Command("SHOW BREAKPOINTS", "Show breakpoints")]
	public class ShowBreakpointsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowBreakpoints ();
		}
	}

	[Command("SHOW THREADGROUPS", "Show thread groups")]
	public class ShowThreadGroupsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowThreadGroups ();
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
			context.CreateThreadGroup (name);
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

			context.AddToThreadGroup (name, threads);
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

			context.RemoveFromThreadGroup (name, threads);
		}
	}

	[Command("BREAKPOINT ENABLE", "Enable breakpoint")]
	public class BreakpointEnableCommand : Command
	{
		BreakpointNumberExpression breakpoint_number_expr;

		public BreakpointEnableCommand (BreakpointNumberExpression breakpoint_number_expr)
		{
			this.breakpoint_number_expr = breakpoint_number_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Breakpoint breakpoint = (Breakpoint) breakpoint_number_expr.Resolve (context);
			breakpoint.Enabled = true;
		}
	}

	[Command("BREAKPOINT DISABLE", "Disable breakpoint")]
	public class BreakpointDisableCommand : Command
	{
		BreakpointNumberExpression breakpoint_number_expr;

		public BreakpointDisableCommand (BreakpointNumberExpression breakpoint_number_expr)
		{
			this.breakpoint_number_expr = breakpoint_number_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Breakpoint breakpoint = (Breakpoint) breakpoint_number_expr.Resolve (context);
			breakpoint.Enabled = false;
		}
	}

	[Command("BREAKPOINT DELETE", "Delete breakpoint")]
	public class BreakpointDeleteCommand : Command
	{
		BreakpointNumberExpression breakpoint_number_expr;

		public BreakpointDeleteCommand (BreakpointNumberExpression breakpoint_number_expr)
		{
			this.breakpoint_number_expr = breakpoint_number_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Breakpoint breakpoint = (Breakpoint) breakpoint_number_expr.Resolve (context);
			context.DeleteBreakpoint (breakpoint);
		}
	}

	[Command("MODULE", "Change module parameters")]
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

			context.ModuleOperations (modules, operations);
		}
	}

	[Command("BREAK", "Insert breakpoint")]
	public class BreakCommand : Command
	{
		ThreadGroupExpression thread_group_expr;
		SourceExpression source_expr;

		public BreakCommand (ThreadGroupExpression thread_group_expr, SourceExpression source_expr)
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

			int index = context.InsertBreakpoint (group, location);
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

	public class SaveCommand : Command
	{
		string filename;

		public SaveCommand (string filename)
		{
			this.filename = filename;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.Save (filename);
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
}
