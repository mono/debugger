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

		protected bool resolved;

		protected virtual ITargetType DoEvaluateType (ScriptingContext context)
		{
			return EvaluateVariable (context).Type;
		}

		public ITargetType EvaluateType (ScriptingContext context)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			try {
				ITargetType type = DoEvaluateType (context);
				if (type == null)
					throw new ScriptingException (
						"Cannot get type of expression `{0}'", Name);

				return type;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException (
					"Location of variable `{0}' is invalid: {1}",
					Name, ex.Message);
			}
		}

		protected virtual object DoEvaluate (ScriptingContext context)
		{
			return DoEvaluateVariable (context);
		}

		public object Evaluate (ScriptingContext context)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			object result = DoEvaluate (context);
			if (result == null)
				throw new ScriptingException (
					"Cannot evaluate expression `{0}'", Name);

			return result;
		}

		protected virtual ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return null;
		}

		public ITargetObject EvaluateVariable (ScriptingContext context)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			try {
				ITargetObject retval = DoEvaluateVariable (context);
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

		protected virtual SourceLocation DoEvaluateLocation (ScriptingContext context,
								     Expression[] types)
		{
			return null;
		}

		public SourceLocation EvaluateLocation (ScriptingContext context,
							Expression [] types)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			try {
				SourceLocation location = DoEvaluateLocation (context, types);
				if (location == null)
					throw new ScriptingException (
						"Expression `{0}' is not a method", Name);

				return location;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException (
					"Location of variable `{0}' is invalid: {1}",
					Name, ex.Message);
			}
		}

		protected virtual bool DoAssign (ScriptingContext context, ITargetObject obj)
		{
			return false;
		}

		public void Assign (ScriptingContext context, ITargetObject obj)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			bool ok = DoAssign (context, obj);
			if (!ok)
				throw new ScriptingException (
					"Expression `{0}' is not an lvalue", Name);
		}

		protected virtual Expression DoResolveType (ScriptingContext context)
		{
			return null;
		}

		public Expression ResolveType (ScriptingContext context)
		{
			Expression expr = DoResolveType (context);
			if (expr == null)
				throw new ScriptingException (
					"Expression `{0}' is not a type.", Name);

			return expr;
		}

		public Expression TryResolveType (ScriptingContext context)
		{
			try {
				return DoResolveType (context);
			} catch (ScriptingException) {
				return null;
			} catch (TargetException) {
				return null;
			}
		}

		protected abstract Expression DoResolve (ScriptingContext context);

		public Expression Resolve (ScriptingContext context)
		{
			Expression expr = DoResolve (context);
			if (expr == null)
				throw new ScriptingException (
					"Expression `{0}' is not a variable or value.", Name);

			return expr;
		}

		public Expression TryResolve (ScriptingContext context)
		{
			try {
				return DoResolve (context);
			} catch (ScriptingException) {
				return null;
			} catch (TargetException) {
				return null;
			}
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

		protected override Expression DoResolve (ScriptingContext context)
		{
			resolved = true;
			return this;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (val.GetType ()))
				return null;

			return frame.Language.CreateInstance (frame, val);
		}

		protected override object DoEvaluate (ScriptingContext context)
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

		protected override Expression DoResolve (ScriptingContext context)
		{
			resolved = true;
			return this;
		}

		protected override object DoEvaluate (ScriptingContext context)
		{
			return val;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (typeof (string)))
				return null;

			return frame.Language.CreateInstance (frame, val);
		}
	}

	public class TypeExpression : Expression
	{
		ITargetType type;

		public TypeExpression (ITargetType type)
		{
			this.type = type;
			resolved = true;
		}

		public override string Name {
			get { return type.Name; }
		}

		protected override Expression DoResolveType (ScriptingContext context)
		{
			return this;
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return this;
		}

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			return type;
		}

		protected override object DoEvaluate (ScriptingContext context)
		{
			return type;
		}
	}

	public class VariableAccessExpression : Expression
	{
		IVariable var;

		public VariableAccessExpression (IVariable var)
		{
			this.var = var;
			resolved = true;
		}

		public override string Name {
			get { return var.Name; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			resolved = true;
			return this;
		}

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			return var.Type;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return context.CurrentFrame.GetVariable (var);
		}

		protected override bool DoAssign (ScriptingContext context, ITargetObject obj)
		{
			if (!var.CanWrite)
				return false;

			if (var.Type != obj.Type)
				throw new ScriptingException (
					"Type mismatch: cannot assign expression of type " +
					"`{0}' to variable `{1}', which is of type `{2}'.",
					obj.Type.Name, Name, var.Type.Name);

			var.SetObject (context.CurrentFrame.Frame, obj);
			return true;
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

		Expression LookupMember (ScriptingContext context, FrameHandle frame)
		{
			IMethod method = frame.Frame.Method;
			if ((method == null) || (method.DeclaringType == null))
				return null;

			ITargetObject instance = null;
			if (method.HasThis)
				instance = (ITargetObject) frame.GetVariable (method.This);

			return StructAccessExpression.FindMember (
				method.DeclaringType, frame.Frame,
				(ITargetStructObject) instance, name);
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			IVariable var = frame.GetVariableInfo (name, false);
			if (var != null)
				return new VariableAccessExpression (var);

			return LookupMember (context, frame);
		}

		protected override Expression DoResolveType (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			ITargetType type = frame.Language.LookupType (frame.Frame, name);
			if (type != null)
				return new TypeExpression (type);

			return null;
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

		protected override Expression DoResolve (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;

			Expression ltype = left.TryResolveType (context);
			if (ltype != null) {
				ITargetStructType stype = ltype.EvaluateType (context)
					as ITargetStructType;
				if (stype == null)
					throw new ScriptingException (
						"`{0}' is not a struct or class", ltype.Name);

				return StructAccessExpression.FindMember (
					stype, frame, null, name);
			}

			Expression lexpr = left.TryResolve (context);
			if (lexpr == null)
				throw new ScriptingException (
					"No such variable or type: `{0}'", left.Name);

			ITargetStructObject sobj = lexpr.EvaluateVariable (context)
				as ITargetStructObject;
			if (sobj == null)
				throw new ScriptingException (
					"`{0}' is not a struct or class", left.Name);

			return StructAccessExpression.FindMember (
				sobj.Type, frame, sobj, name);
		}

		protected override Expression DoResolveType (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;

			ITargetType the_type;

			Expression ltype = left.TryResolveType (context);
			if (ltype == null)
				the_type = frame.Language.LookupType (frame, Name);
			else {
				string nested = ltype.Name + "+" + name;
				the_type = frame.Language.LookupType (frame, nested);
			}

			if (the_type == null)
				return null;

			return new TypeExpression (the_type);
		}
	}

	public class MethodGroupExpression : Expression
	{
		ITargetStructType stype;
		ITargetStructObject instance;
		ILanguage language;
		string name;
		ArrayList methods;

		public MethodGroupExpression (ITargetStructType stype, string name,
					      ITargetStructObject instance,
					      ILanguage language, ArrayList methods)
		{
			this.stype = stype;
			this.instance = instance;
			this.language = language;
			this.name = name;
			this.methods = methods;
			resolved = true;
		}

		public override string Name {
			get { return stype.Name + "." + name; }
		}

		public bool IsStatic {
			get { return instance == null; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return this;
		}

		protected override SourceLocation DoEvaluateLocation (ScriptingContext context,
								      Expression[] types)
		{
			ITargetMethodInfo method = OverloadResolve (context, types);
			return new SourceLocation (method.Type.Source);
		}

		public ITargetFunctionObject EvaluateMethod (ScriptingContext context,
							     StackFrame frame,
							     Expression[] arguments)
		{
			ITargetMethodInfo method = OverloadResolve (context, arguments);

			if (method.IsStatic)
				return stype.GetStaticMethod (frame, method.Index);
			else if (!IsStatic)
				return instance.GetMethod (method.Index);
			else
				throw new ScriptingException (
					"Instance method {0} cannot be used in " +
					"static context.", Name);
		}

		protected ITargetMethodInfo OverloadResolve (ScriptingContext context,
							     Expression[] types)
		{
			ArrayList candidates = new ArrayList ();

			foreach (ITargetMethodInfo method in methods) {
				if ((types != null) &&
				    (method.Type.ParameterTypes.Length != types.Length))
					continue;

				candidates.Add (method);
			}

			if (candidates.Count == 1)
				return (ITargetMethodInfo) candidates [0];

			if (candidates.Count == 0)
				throw new ScriptingException (
					"No overload of method `{0}' has {1} arguments.",
					Name, types.Length);

			if (types == null)
				throw new ScriptingException (
					"Ambiguous method `{0}'; need to use " +
					"full name", Name);

			ITargetMethodInfo match = OverloadResolve (
				context, language, stype, types, candidates);

			if (match == null)
				throw new ScriptingException (
					"Ambiguous method `{0}'; need to use " +
					"full name", Name);

			return match;
		}

		public static ITargetMethodInfo OverloadResolve (ScriptingContext context,
								 ILanguage language,
								 ITargetStructType stype,
								 Expression[] types,
								 ArrayList candidates)
		{
			// We do a very simple overload resolution here
			ITargetType[] argtypes = new ITargetType [types.Length];
			for (int i = 0; i < types.Length; i++)
				argtypes [i] = types [i].EvaluateType (context);

			// Ok, no we need to find an exact match.
			ITargetMethodInfo match = null;
			foreach (ITargetMethodInfo method in candidates) {
				bool ok = true;
				for (int i = 0; i < types.Length; i++) {
					if (method.Type.ParameterTypes [i].TypeHandle != argtypes [i].TypeHandle) {
						ok = false;
						break;
					}
				}

				if (!ok)
					continue;

				// We need to find exactly one match
				if (match != null)
					return null;

				match = method;
			}

			return match;
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

		protected override Expression DoResolve (ScriptingContext context)
		{
			object lobj, robj;

			lobj = left.Resolve (context);
			robj = right.Resolve (context);

			// Console.WriteLine ("bin eval: {0} ({1}) and {2} ({3})", lobj, lobj.GetType(), robj, robj.GetType());
			return DoEvaluate (context, lobj, robj);
		}
	}
#endif
}
