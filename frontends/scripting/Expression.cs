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
		int val;

		public NumberExpression (int val)
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

	public class ProcessExpression : Expression
	{
		int number;

		public ProcessExpression (int number)
		{
			this.number = number;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.ProcessByID (number);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), number);
		}
	}

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

	public class RegisterExpression : Expression
	{
		int number;
		ProcessExpression process_expr;
		string register;

		public RegisterExpression (ProcessExpression process_expr, int number, string register)
		{
			this.process_expr = process_expr;
			this.number = number;
			this.register = register;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			return process.GetRegister (number, register);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2},{3})", GetType (), process_expr, number,
					      register);
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

	public class SourceExpression : Expression
	{
		ProcessExpression process_expr;
		int line, number, history_id;
		string file_name, name;

		public SourceExpression (ProcessExpression process_expr, int number, string name, int line)
		{
			this.process_expr = process_expr;
			this.history_id = -1;
			this.number = number;
			this.name = name;
			this.line = line;
		}

		public SourceExpression (ProcessExpression process_expr, int number, int line)
		{
			this.process_expr = process_expr;
			this.history_id = -1;
			this.number = number;
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

			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			StackFrame frame = process.GetFrame (number);

			IMethod method = frame.Method;
			if ((method == null) || !method.HasSource)
				throw new ScriptingException ("No current method.");

			IMethodSource source = method.Source;
			if (name == null) {
				if (source.IsDynamic || (frame.SourceAddress == null))
					throw new ScriptingException ("Current method has no source code.");

				if (line == -1)
					return frame.SourceAddress.Location;
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
}
