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

	public abstract class Expression
	{
		protected abstract object DoResolve (ScriptingContext context);

		public object Resolve (ScriptingContext context)
		{
			object retval = DoResolve (context);

			if (retval == null)
				throw new ScriptingException ("Can't resolve command: {0}", this);

			return retval;
		}
	}

	public class CommandExpression : Command
	{
		Expression expression;

		public CommandExpression (Expression expression)
		{
			this.expression = expression;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			object retval = expression.Resolve (context);
			context.Print (retval);
		}
	}

	public class ProcessExpression : Expression
	{
		int number;

		public ProcessExpression (int number)
		{
			this.number = number;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			if (number == -1)
				return context.CurrentProcess;

			foreach (ProcessHandle proc in context.Processes)
				if (proc.ID == number)
					return proc;

			throw new ScriptingException ("No such process: {0}", number);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), number);
		}
	}

	public class FrameExpression : Expression
	{
		int number;
		ProcessExpression process_expr;

		public FrameExpression (ProcessExpression process_expr, int number)
		{
			this.process_expr = process_expr;
			this.number = number;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			return process.GetFrame (number);
		}
	}

	public class StartExpression : Expression
	{
		string[] args;
		int pid;

		public StartExpression (string[] args)
		{
			this.args = args;
			this.pid = -1;
		}

		public StartExpression (string[] args, int pid)
		{
			this.args = args;
			this.pid = pid;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.Start (args, pid);
		}
	}

	public class SelectProcessExpression : Expression
	{
		ProcessExpression process_expr;

		public SelectProcessExpression (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			context.CurrentProcess = process;
			return process;
		}
	}

	public class BackgroundProcessExpression : Expression
	{
		ProcessExpression process_expr;

		public BackgroundProcessExpression (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Background ();
			return process;
		}
	}

	public class StopProcessExpression : Expression
	{
		ProcessExpression process_expr;

		public StopProcessExpression (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Stop ();
			return process;
		}
	}

	public class StepCommand : Command
	{
		ProcessExpression process_expr;
		WhichStepCommand which;

		public StepCommand (ProcessExpression process_expr, WhichStepCommand which)
		{
			this.process_expr = process_expr;
			this.which = which;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (which);
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
			StackFrame[] backtrace = process.GetBacktrace ();
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
			context.Print (process.CurrentFrame);
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
			context.Print (process.CurrentFrame);
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
		ProcessExpression process_expr;

		public ShowRegistersCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			string[] names = process.Architecture.RegisterNames;
			int[] indices = process.Architecture.RegisterIndices;
			long[] regs = process.GetRegisters (indices);
			for (int i = 0; i < regs.Length; i++)
				context.Print ("%{0} = 0x{1:x}", names [indices [i]], regs [i]);
		}
	}

	public class BreakCommand : Command
	{
		string identifier;

		public BreakCommand (string identifier)
		{
			this.identifier = identifier;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Console.WriteLine ("BREAK: {0}", identifier);
			context.InsertBreakpoint (identifier);
		}
	}
}
