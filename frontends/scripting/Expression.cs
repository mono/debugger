using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
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

	public class NumberExpression : Expression
	{
		object val;

		public NumberExpression (int val)
		{
			this.val = val;
		}

		public NumberExpression (long val)
		{
			this.val = val;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return this.val;
		}

		public override string ToString ()
		{
			return String.Format ("{0} {1:x}", GetType(), this.val);
		}
	}

	public class StringExpression : Expression
	{
		string val;

		public StringExpression (string val)
		{
			this.val = val;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return this.val;
		}

		public override string ToString ()
		{
			return String.Format ("{0} {1:x}", GetType(), this.val);
		}
	}

	// So you can extend this by just creating a subclass
	// of BinaryOperator that implements DoEvaluate and
	// a constructor, but you'll need to add a new rule to
	// the parser of the form
	//
	// expression: my_param_kind MY_OP_TOKEN my_param_kind 
	//             { $$ = new MyBinarySubclass ((MyParam) $1, (MyParam) $3); }
	//
	// If you want to extend on of { +, -, *, /} for non-integers,
	// like supporting "a" + "b" = "ab", then larger changes would
	// be needed.

	public class BinaryOperator : Expression
	{
		public enum Kind { Mult, Plus, Minus, Div };

		protected Kind kind;
		protected Expression left, right;

		public BinaryOperator (Kind kind, Expression left, Expression right)
		{
			this.kind = kind;
			this.left = left;
			this.right = right;
		}

		protected object DoEvaluate (ScriptingContext context, object lobj, object robj)
		{
			switch (kind) {
			case Kind.Mult:
				return (int) lobj * (int) robj;
			case Kind.Plus:
				return (int) lobj + (int) robj;
			case Kind.Minus:
				return (int) lobj - (int) robj;
			case Kind.Div:
				return (int) lobj / (int) robj;
			}

			throw new ScriptingException ("Unknown binary operator kind: {0}", kind);
		}

		protected override object DoResolve (ScriptingContext context)
		{
			object lobj, robj;

			lobj = left.Resolve (context);
			robj = right.Resolve (context);

			// Console.WriteLine ("bin eval: {0} ({1}) and {2} ({3})", lobj, lobj.GetType(), robj, robj.GetType());
			return DoEvaluate (context, lobj, robj);
		}
	}

	[Expression("process_expression", "Process",
		    "Syntax:  [@<number>]\n\n" +
		    "Specifies on which process a command operates.\n\n" +
		    "This argument may be omitted in which case the current process\n" +
		    "is used.\n" +
		    "To change the current process, use the `process' command.\n" +
		    "To get a list of all processes, use `show processes'.")]
	public class ProcessExpression : Expression
	{
		int number;

		public ProcessExpression (int number)
		{
			this.number = number;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.GetProcess (number);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), number);
		}
	}

	[Expression("frame_expression", "Stack frame",
		    "Syntax:  [<process_expression>] [#<number>]\n\n" +
		    "Specifies the stack frame a command operates on.\n\n" +
		    "Both the process expression and the frame number are optional.\n" +
		    "If no process is specified, the current process is used; if the\n" +
		    "frame number is omitted, the current frame of that process is used.\n\n" +
		    "Examples:  #3\n" +
		    "           @1\n" +
		    "           @2 #4"
		)]
	public class FrameExpression : Expression
	{
		ProcessExpression process_expr;
		int number;

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

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2})", GetType(), process_expr, number);
		}
	}

	[Expression("breakpoint_expression", "Breakpoint number")]
	public class BreakpointNumberExpression : Expression
	{
		int number;

		public BreakpointNumberExpression (int number)
		{
			this.number = number;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.GetBreakpoint (number);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType(), number);
		}
	}

	public class ProgramArgumentsExpression : Expression
	{
		string[] args;

		public ProgramArgumentsExpression (string[] args)
		{
			this.args = args;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return args;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType(), String.Join (":", args));
		}
	}

	[Expression("thread_group_expression", "Thread group",
		    "Syntax:  ['<' IDENTIFIER '>']\n" +
		    "         ['<main>']\n\n" +
		    "Thread groups are used to give one or more processes a symbolic name.\n" +
		    "They're used to specify on which threads a breakpoint \"breaks\".\n\n" +
		    "If no thread group is specified, `main' is used which is always set to\n" +
		    "the application's main thread.\n\n" +
		    "To get a list of all thread groups, use `show threadgroups'\n" +
		    "Use the `threadgroup' command to create/modifiy thread groups\n" +
		    "(see `help threadgroup' for details)\n\n" +
		    "Example:  <foo>\n"
		    )]
	public class ThreadGroupExpression : Expression
	{
		string name;

		public ThreadGroupExpression (string name)
		{
			this.name = name;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ThreadGroup group = ThreadGroup.ThreadGroupByName (name);
			if (group == null)
				throw new ScriptingException ("No such thread group.");

			return group;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), name);
		}
	}

	public class ExpressionGroup : Expression
	{
		Expression expr;

		public ExpressionGroup (Expression expr)
		{
			this.expr = expr;
		}

		public override string ToString()
		{
			return '(' + expr.ToString() + ')';
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return expr.Resolve (context);
		}
	}

	public class ArrayLengthExpression : Expression
	{
		VariableExpression var_expr;

		public ArrayLengthExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);

			return obj.UpperBound - obj.LowerBound;
		}
	}

	public class ArrayLowerBoundExpression : Expression
	{
		VariableExpression var_expr;

		public ArrayLowerBoundExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);

			return obj.LowerBound;
		}
	}

	public class ArrayUpperBoundExpression : Expression
	{
		VariableExpression var_expr;

		public ArrayUpperBoundExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);

			return obj.UpperBound;
		}
	}

	[Expression("source_expression", "Source file expression",
		    "Specifies a location in the source code.\n\n" +
		    "Syntax:  [<frame_expression>] IDENTIFIER [':' INTEGER]\n\n" +
		    "This is used when debugging managed application.  It is used to search\n" +
		    "a method in the current class.\n\n" +
		    "Searches for a method with the requested name in the current class.\n" +
		    "If more than one method is found, all of them are printed and added to\n" +
		    "the history.  If just one single method is found, the optional line\n" +
		    "number specifies a specific line in that method.\n\n" +
		    "Examples:  Test\n" +
		    "           Test : 45\n\n" +
		    "Syntax:  STRING\n\n" +
		    "A fully qualified method name.\n\n" +
		    "Examples:  \"X.Foo()\"\n" +
		    "           \"unmanaged_function\"\n\n" +
		    "Syntax:  STRING ':' INTEGER\n\n" +
		    "File name and line number.\n\n" +
		    "Example:   \"Foo.cs\" : 45\n\n" +
		    "Syntax:  ! INTEGER\n\n" +
		    "Specifies an entry from the search history.\n\n")]
	public class SourceExpression : Expression
	{
		FrameExpression frame_expr;
		int line, history_id;
		string file_name, name;

		public SourceExpression (FrameExpression frame_expr, string name, int line)
		{
			this.history_id = -1;
			this.frame_expr = frame_expr;
			this.name = name;
			this.line = line;
		}

		public SourceExpression (FrameExpression frame_expr, int line)
		{
			this.history_id = -1;
			this.frame_expr = frame_expr;
			this.line = line;
		}

		public SourceExpression (int history_id)
		{
			this.history_id = history_id;
		}

		public SourceExpression (string file_name, int line)
		{
			this.file_name = file_name;
			this.line = line;
		}

		public SourceExpression (string file_name)
		{
			this.file_name = file_name;
			this.line = -1;
		}

		public SourceLocation ResolveLocation (ScriptingContext context)
		{
			object result = Resolve (context);
			if (result == null)
				throw new ScriptingException ("No such method.");

			SourceLocation location = result as SourceLocation;
			if (location != null)
				return location;

			context.AddMethodSearchResult ((SourceMethod []) result);
			return null;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			if (history_id > 0)
				return context.GetMethodSearchResult (history_id);

			if (file_name != null) {
				if (line == -1)
					return context.FindLocation (file_name);
				else
					return context.FindLocation (file_name, line);
			}

			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			IMethod method = frame.Frame.Method;
			if ((method == null) || !method.HasSource)
				throw new ScriptingException ("No current method.");

			IMethodSource source = method.Source;
			if (name == null) {
				if (source.IsDynamic || (frame.Frame.SourceAddress == null))
					throw new ScriptingException ("Current method has no source code.");

				if (line == -1)
					return frame.Frame.SourceAddress.Location;
				else
					return context.FindLocation (source.SourceFile.FileName, line);
			}

			SourceMethod[] result = source.MethodLookup (name);

			if (result.Length == 0)
				throw new ScriptingException ("No method matches your query.");
			else if (result.Length > 1)
				return result;

			if (line == -1)
				return new SourceLocation (result [0]);

			if ((line < result [0].StartRow) || (line > result [0].EndRow))
				throw new ScriptingException ("Requested line number outside of method.");

			return new SourceLocation (result [0], line);
		}
	}

	[Expression("module_list_expression", "List of modules",
		    "This is a comma separated list of module numbers\n" +
		    "(from `show modules')\n\n" +
		    "Examples:  1\n" +
		    "           1,2,3\n")]
	public class ModuleListExpression : Expression
	{
		int[] modules;

		public ModuleListExpression (int[] modules)
		{
			this.modules = modules;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.GetModules (modules);
		}

		public override string ToString ()
		{
			string[] temp = new string [modules.Length];
			for (int i = 0; i < modules.Length;i ++)
				temp [i] = modules [i].ToString ();
			return String.Format ("{0} ({1})", GetType(), String.Join (":", temp));
		}
	}

	[Expression("source_list_expression", "List of source files",
		    "This is a comma separated list of source file numbers\n" +
		    "(from `show sources')\n\n" +
		    "Examples:  1\n" +
		    "           1,2,3\n")]
	public class SourceListExpression : Expression
	{
		int[] sources;

		public SourceListExpression (int[] sources)
		{
			this.sources = sources;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.GetSources (sources);
		}

		public override string ToString ()
		{
			string[] temp = new string [sources.Length];
			for (int i = 0; i < sources.Length;i ++)
				temp [i] = sources [i].ToString ();
			return String.Format ("{0} ({1})", GetType(), String.Join (":", temp));
		}
	}

	[Expression("process_list_expression", "List of processes",
		    "This is a comma separated list of process numbers\n" +
		    "(from `show processes', without the `@'s)\n\n" +
		    "Examples:  1\n" +
		    "           1,2,3\n")]
	public class ProcessListExpression : Expression
	{
		int[] processes;

		public ProcessListExpression (int[] processes)
		{
			this.processes = processes;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.GetProcesses (processes);
		}

		public override string ToString ()
		{
			string[] temp = new string [processes.Length];
			for (int i = 0; i < processes.Length;i ++)
				temp [i] = processes [i].ToString ();
			return String.Format ("{0} ({1})", GetType(), String.Join (":", temp));
		}
	}

	[Expression("module_operations", "List of module operations",
		    "Whitespace separated list of module operations:\n\n" +
		    "  ignore           Completely ignore the module, do not step into any of\n" +
		    "                   its methods and do not include its method names in\n" +
		    "                   stack traces.\n\n" +
		    "  unignore         The contrary of `ignore'.\n" +
		    "  !ignore\n\n" +
		    "  step             Step into methods from this module while single-stepping\n\n" +
		    "  !step            The contrary of `step'.\n" +
		    "                   Note that the debugger still shows method names and source\n" +
		    "                   code in stack traces, for instance when the target hits a\n" +
		    "                   breakpoint somewhere inside this module.\n\n" +
		    "Example:  !ignore step\n")]
	public class ModuleOperationListExpression : Expression
	{
		ModuleOperation[] operations;

		public ModuleOperationListExpression (ModuleOperation[] operations)
		{
			this.operations = operations;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return operations;
		}

		public override string ToString ()
		{
			string[] temp = new string [operations.Length];
			for (int i = 0; i < operations.Length;i ++)
				temp [i] = operations [i].ToString ();
			return String.Format ("{0} ({1})", GetType(), String.Join (":", temp));
		}
	}
}
