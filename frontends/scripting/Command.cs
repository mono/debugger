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
		string[] args;
		int pid;

		public StartCommand (string[] args)
		{
			this.args = args;
			this.pid = -1;
		}

		public StartCommand (string[] args, int pid)
		{
			this.args = args;
			this.pid = pid;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (!context.IsSynchronous)
				throw new ScriptingException ("This command cannot be used in the GUI.");
			if (pid != -1)
				throw new ScriptingException ("Attaching is not yet implemented.");
			context.Start (args);
		}
	}

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

	public class ShowModulesCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowModules ();
		}
	}

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

	public class ShowBreakpointsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowBreakpoints ();
		}
	}

	public class ShowThreadGroupsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowThreadGroups ();
		}
	}

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

	public class BreakpointEnableCommand : Command
	{
		int index;

		public BreakpointEnableCommand (int index)
		{
			this.index = index;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Breakpoint breakpoint = context.FindBreakpoint (index);
			breakpoint.Enabled = true;
		}
	}

	public class BreakpointDisableCommand : Command
	{
		int index;

		public BreakpointDisableCommand (int index)
		{
			this.index = index;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Breakpoint breakpoint = context.FindBreakpoint (index);
			breakpoint.Enabled = false;
		}
	}

	public class BreakpointDeleteCommand : Command
	{
		int index;

		public BreakpointDeleteCommand (int index)
		{
			this.index = index;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.DeleteBreakpoint (index);
		}
	}

	public class ModuleOperationCommand : Command
	{
		ModuleListExpression module_list_expr;
		ModuleOperation[] operations;

		public ModuleOperationCommand (ModuleListExpression module_list_expr,
					       ModuleOperation[] operations)
		{
			this.module_list_expr = module_list_expr;
			this.operations = operations;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Module[] modules = (Module []) module_list_expr.Resolve (context);

			context.ModuleOperations (modules, operations);
		}
	}

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

	public class HelpCommand : Command
	{
		string type;
		
		public HelpCommand (string t)
		{
			type = t;
		}
		
		protected override void DoExecute (ScriptingContext context)
		{
			switch (type){
			case "show":
				context.Print (
					"show processes          Processes\n" +
					"show registers [p][f]    CPU register contents for [p]rocess/[f]rame\n"+
					"show parameters [p][f]  Parameters for [p]rocess/[f]rame\n"+
					"show locals [p][f]      Local variables for [p]rocess/[f]rame\n"+
					"show modules            The list of loaded modules\n" +
					"show threadgroups       The list of threadgroups\n"+
					"show type <expr>        displays the type for an expression\n");
				break;
			case "":
				context.Print (
					"    backtrace            prints out the backtrace\n" +
					"    frame [proc][fn]     Selects frame\n" + 
					"    c, continue          continue execution\n" +
					"    s, step              single steps\n" +
					"    stepi                single step, at instruction level\n" + 
					"    n, next              next line\n" +
					"    nexti                next line, at instruction level\n" +
					"    finish               runs until the end of the current method\n" + 
					"    up [N]               \n" +
					"    down [N]             \n" +
					"    kill PID             \n" +
					"    show OPT             Shows some information, use help show for details\n" +
					"    print expr           Prints the value of expression\n" + 
					"    \n" +
					"Breakpoints:\n" +
					"    break [tg][func]     inserts a breakpoint, use help break\n" +
					"    breakpoint           manages the breakoints, use help break\n" +
					"    \n" +	          
					"    print EXPR           Prints the expression\n" +
					"    quit                 quits the debugger");
				break;
			default:
				break;
			}
		}
	}

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
