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
	public abstract class Expression
	{
		public abstract string Name {
			get;
		}

		protected virtual bool DoResolveBase (ScriptingContext context)
		{
			return true;
		}

		protected void ResolveBase (ScriptingContext context)
		{
			bool ok;
			try {
				ok = DoResolveBase (context);
			} catch {
				ok = false;
			}
			if (!ok)
				throw new ScriptingException (
					"Can't resolve expression `{0}.", Name);
		}

		protected virtual ITargetType DoResolveType (ScriptingContext context)
		{
			return null;
		}

		public ITargetType ResolveType (ScriptingContext context)
		{
			ResolveBase (context);

			ITargetType type = DoResolveType (context);
			if (type == null)
				throw new ScriptingException (
					"Can't get type of expression `{0}'.", Name);

			return type;
		}

		protected virtual ITargetObject DoResolveVariable (ScriptingContext context)
		{
			return null;
		}

		public ITargetObject ResolveVariable (ScriptingContext context)
		{
			ResolveBase (context);

			try {
				ITargetObject retval = DoResolveVariable (context);
				if (retval == null)
					throw new ScriptingException (
						"Expression `{0}' is not a variable", Name);

				return retval;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException (
					"Location of variable `{0}' is invalid: {1}",
					Name, ex.Message);
			}
		}

		protected virtual ITargetFunctionObject DoResolveMethod (ScriptingContext context, Expression[] args)
		{
			return null;
		}

		public ITargetFunctionObject ResolveMethod (ScriptingContext context, Expression[] args)
		{
			ResolveBase (context);

			Expression[] types = null;
			if (args != null) {
				types = new Expression [args.Length];
				for (int i = 0; i < args.Length; i++) {
					types [i] = args [i] as Expression;
					if (types [i] == null)
						throw new ScriptingException (
							"Argument {0} is not a type or variable: `{1}'.",
							i, args [i]);
				}
			}

			try {
				ITargetFunctionObject retval = DoResolveMethod (context, types);
				if (retval == null)
					throw new ScriptingException (
						"Expression does not resolve to a method: `{0}'", Name);

				return retval;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException (
					"Location of variable `{0}' is invalid: {1}",
					Name, ex.Message);
			}
		}

		protected virtual bool DoAssign (ScriptingContext context, object obj)
		{
			return false;
		}

		public void Assign (ScriptingContext context, object obj)
		{
			ResolveBase (context);

			bool ok;
			try {
				ok = DoAssign (context, obj);
			} catch {
				ok = false;
			}

			if (!ok)
				throw new ScriptingException (
					"Expression `{0}' is not an lvalue", Name);
		}

		protected abstract object DoResolve (ScriptingContext context);

		public object Resolve (ScriptingContext context)
		{
			object retval = DoResolve (context);

			if (retval == null)
				throw new ScriptingException ("Can't resolve command: {0}", this);

			return retval;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), Name);
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

		public override string Name {
			get { return val.ToString (); }
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return val;
		}
	}

	public class StringExpression : Expression
	{
		string val;

		public StringExpression (string val)
		{
			this.val = val;
		}

		public override string Name {
			get { return val; }
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return val;
		}
	}

	public class TypeExpression : Expression
	{
		ITargetType type;

		public TypeExpression (ITargetType type)
		{
			this.type = type;
		}

		public override string Name {
			get { return type.Name; }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			return type;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return ResolveType (context);
		}
	}

	public class SimpleNameExpression : Expression
	{
		string name;

		public SimpleNameExpression (string name)
		{
			this.name = name;
		}

		public override string Name {
			get { return name; }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			IVariable var = frame.GetVariableInfo (name, false);
			if (var != null)
				return var.Type;

			if (frame.Frame.Language == null)
				return null;

			return frame.Frame.Language.LookupType (frame.Frame, name);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			IVariable var = frame.GetVariableInfo (name, false);
			if (var != null)
				return frame.GetVariable (var);

			return null;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetObject obj = DoResolveVariable (context);
			if (obj != null)
				return obj;

			ITargetType type = DoResolveType (context);
			if (type != null)
				return type;

			throw new ScriptingException ("No such variable or type: `{0}'", Name);
		}
	}

	public class MemberAccessExpression : Expression
	{
		Expression left;
		string name;

		public MemberAccessExpression (Expression left, string name)
		{
			this.left = left;
			this.name = name;
		}

		public override string Name {
			get { return left.Name + "." + name; }
		}

		Expression ResolveMemberAccess (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;

			object resolved = left.Resolve (context);

			if (resolved is ITargetObject) {
				ITargetStructObject sobj = resolved as ITargetStructObject;
				if (sobj == null)
					throw new ScriptingException (
						"{0} is not a struct of class", left);

				return new StructAccessExpression (frame, sobj, name);
			}

			ITargetType type = (ITargetType) resolved;
			if (frame.Language != null) {
				string nested = type.Name + "+" + name;
				ITargetType ntype = frame.Language.LookupType (frame, nested);
				if (ntype != null)
					return new TypeExpression (ntype);
			}

			ITargetStructType stype = resolved as ITargetStructType;
			if (stype == null)
				throw new ScriptingException (
					"{0} is not a struct of class", left);

			return new StructAccessExpression (frame, stype, name);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			Expression expr = ResolveMemberAccess (context);

			return expr.ResolveVariable (context);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			Expression expr = ResolveMemberAccess (context);

			return expr.ResolveType (context);
		}

		protected override object DoResolve (ScriptingContext context)
		{
			Expression expr = ResolveMemberAccess (context);

			return expr.Resolve (context);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})", GetType(), left, name);
		}
	}

#if FIXME
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
#endif

#if FIXME
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
			return context.Interpreter.GetBreakpoint (number);
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
		VariableExpression expr;
		string identifier;
		int history_id;
		int line;

		public SourceExpression (VariableExpression expr, int line)
		{
			this.history_id = -1;
			this.expr = expr;
			this.line = line;
		}

		public SourceExpression (string identifier)
		{
			this.history_id = -1;
			this.identifier = identifier;
			this.line = -1;
		}

		public SourceExpression (string identifier, int line)
		{
			this.history_id = -1;
			this.identifier = identifier;
			this.line = line;
		}

		public SourceExpression (int history_id)
		{
			this.history_id = history_id;
			this.expr = null;
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

			if (identifier != null) {
				if (line != -1)
					return context.Interpreter.FindLocation (
						identifier, line);
				else
					return context.Interpreter.FindMethod (identifier);
			}

			ITargetFunctionObject obj = expr.ResolveMethod (context, null);

			SourceMethod source = obj.Type.Source;
			if (source == null)
				throw new ScriptingException ("Method `{0}' has no source code.",
							      expr.Name);

			if (line == -1)
				return new SourceLocation (source);

			if ((line < source.StartRow) || (line > source.EndRow))
				throw new ScriptingException ("Requested line number {0} outside of method (line {1} until {2}).", line, source.StartRow, source.EndRow);

			return new SourceLocation (source, line);
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
			return context.Interpreter.GetModules (modules);
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
			return context.Interpreter.GetSources (sources);
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
			return context.Interpreter.GetProcesses (processes);
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
#endif
}
