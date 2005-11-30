using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontend
{
	public enum LocationType
	{
		Method,
		DelegateInvoke,
		PropertyGetter,
		PropertySetter,
		EventAdd,
		EventRemove
	}

	public abstract class Expression
	{
		public abstract string Name {
			get;
		}

		protected bool resolved;

		protected virtual TargetType DoEvaluateType (ScriptingContext context)
		{
			return EvaluateObject (context).Type;
		}

		public TargetType EvaluateType (ScriptingContext context)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			try {
				TargetType type = DoEvaluateType (context);
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
			return DoEvaluateObject (context);
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

		protected virtual TargetVariable DoEvaluateVariable (ScriptingContext context)
		{
			return null;
		}

		public TargetVariable EvaluateVariable (ScriptingContext context)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}' ({1})", Name,
						GetType ()));

			try {
				TargetVariable retval = DoEvaluateVariable (context);
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

		protected virtual TargetObject DoEvaluateObject (ScriptingContext context)
		{
			TargetVariable var = DoEvaluateVariable (context);
			if (var == null)
				return null;

			return var.GetObject (context.CurrentFrame.Frame);
		}

		public TargetObject EvaluateObject (ScriptingContext context)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}' ({1})", Name,
						GetType ()));

			try {
				TargetObject retval = DoEvaluateObject (context);
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

		protected virtual SourceLocation DoEvaluateSource (ScriptingContext context,
								   LocationType type, Expression[] types)
		{
			TargetFunctionType func = DoEvaluateMethod (context, type, types);
			if (func == null)
				return null;

			SourceMethod source = func.Source;
			if (source == null)
				return null;

			return new SourceLocation (source);
		}

		public SourceLocation EvaluateSource (ScriptingContext context, LocationType type,
						      Expression [] types)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			try {
				SourceLocation location = DoEvaluateSource (context, type, types);
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

		protected virtual TargetFunctionType DoEvaluateMethod (ScriptingContext context,
									LocationType type,
									Expression[] types)
		{
			return null;
		}

		public TargetFunctionType EvaluateMethod (ScriptingContext context,
							   LocationType type, Expression [] types)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			try {
				TargetFunctionType func = DoEvaluateMethod (context, type, types);
				if (func == null)
					throw new ScriptingException (
						"Expression `{0}' is not a method", Name);

				return func;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException (
					"Location of variable `{0}' is invalid: {1}",
					Name, ex.Message);
			}
		}

		protected virtual bool DoAssign (ScriptingContext context, TargetObject obj)
		{
			return false;
		}

		public void Assign (ScriptingContext context, TargetObject obj)
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

		protected virtual Expression DoResolveMethod (ScriptingContext context)
		{
			return DoResolve (context);
		}

		public Expression ResolveMethod (ScriptingContext context)
		{
			Expression expr = DoResolveMethod (context);
			if (expr == null)
				throw new ScriptingException (
					"Expression `{0}' is not a method.", Name);

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

	public class NumberExpression : PointerExpression
	{
		object val;

		public NumberExpression (int val)
		{
			this.val = val;
		}

		public NumberExpression (uint val)
		{
			this.val = val;
		}

		public NumberExpression (long val)
		{
			this.val = val;
		}

		public NumberExpression (ulong val)
		{
			this.val = val;
		}

		public NumberExpression (float val)
		{
			this.val = val;
		}

		public NumberExpression (double val)
		{
			this.val = val;
		}

		public NumberExpression (decimal val)
		{
			this.val = val;
		}

		public long Value {
			get {
				if (val is int)
					return (long) (int) val;
				else if (val is uint)
					return (long) (uint) val;
				else if (val is ulong)
					return (long) (ulong) val;
				else
					return (long) val;
			}
		}

		public override string Name {
			get {
				if (val is long)
					return String.Format ("0x{0:x}", (long) val);
				else
					return val.ToString ();
			}
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			resolved = true;
			return this;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (val.GetType ()))
				throw new ScriptingException ("Cannot instantiate value '{0}' in the current frame's language", Name);

			return frame.Language.CreateInstance (frame.TargetAccess, val);
		}

		public override TargetAddress EvaluateAddress (ScriptingContext context)
		{
			return new TargetAddress (context.AddressDomain, Value);
		}

		protected override object DoEvaluate (ScriptingContext context)
		{
			return val;
		}

		public override string ToString ()
		{
			return Name;
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
			get { return '"' + val + '"'; }
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

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (typeof (string)))
				throw new ScriptingException ("Cannot instantiate value '{0}' in the current frame's language", Name);

			return frame.Language.CreateInstance (frame.TargetAccess, val);
		}

		public override string ToString ()
		{
			return Name;
		}
	}

	public class BoolExpression : Expression
	{
		bool val;

		public BoolExpression (bool val)
		{
			this.val = val;
		}

		public override string Name {
			get { return val.ToString(); }
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

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (typeof (bool)))
				throw new ScriptingException ("Cannot instantiate value '{0}' in the current frame's language", Name);

			return frame.Language.CreateInstance (frame.TargetAccess, val);
		}

		public override string ToString ()
		{
			return Name;
		}
	}

	public class NullExpression : Expression
	{
		public override string Name {
			get { return "null"; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			resolved = true;
			return this;
		}

		protected override object DoEvaluate (ScriptingContext context)
		{
			return null;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			throw new InvalidOperationException ();
		}

		public override string ToString ()
		{
			return Name;
		}
	}

	public class ArgumentExpression : Expression
	{
		TargetObject obj;

		public ArgumentExpression (TargetObject obj)
		{
			this.obj = obj;
			resolved = true;
		}

		public override string Name {
			get { return obj.ToString(); }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return this;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			return obj;
		}

		public override string ToString ()
		{
			return Name;
		}
	}

	public class ThisExpression : Expression
	{
		public override string Name {
			get { return "this"; }
		}

		protected FrameHandle frame;
		protected TargetVariable var;

		protected override Expression DoResolve (ScriptingContext context)
		{
			frame = context.CurrentFrame;
			Method method = frame.Frame.Method;
			if (method == null)
				throw new ScriptingException (
					"Keyword `this' not allowed: no current method.");

			if (!method.HasThis)
				throw new ScriptingException (
					"Keyword `this' not allowed: current method is " +
					"either static or unmanaged.");

			var = method.This;
			resolved = true;
			return this;
		}

		protected override TargetVariable DoEvaluateVariable (ScriptingContext context)
		{
			return var;
		}
	}

	public class BaseExpression : ThisExpression
	{
		public override string Name {
			get { return "base"; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			Expression expr = base.DoResolve (context);
			if (expr == null)
				return null;

			if (var.Type.Kind != TargetObjectKind.Class)
				throw new ScriptingException (
					"`base' is only allowed in a class.");
			if (!((TargetClassType) var.Type).HasParent)
				throw new ScriptingException (
					"Current class has no base class.");

			return expr;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			TargetAccess target = context.CurrentFrame.TargetAccess;
			TargetClassObject cobj = (TargetClassObject) base.DoEvaluateObject (context);
			return cobj.GetParentObject (target);
		}
	}

	public class CatchExpression : Expression
	{
		public override string Name {
			get { return "catch"; }
		}

		TargetObject exc;

		protected override Expression DoResolve (ScriptingContext context)
		{
			exc = context.CurrentFrame.Frame.ExceptionObject;
			if (exc == null)
				throw new ScriptingException ("No current exception.");

			resolved = true;
			return this;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			return exc;
		}
	}

	public class TypeExpression : Expression
	{
		TargetType type;

		public TypeExpression (TargetType type)
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

		protected override TargetType DoEvaluateType (ScriptingContext context)
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
		TargetVariable var;

		public VariableAccessExpression (TargetVariable var)
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

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			return var.Type;
		}

		protected override TargetVariable DoEvaluateVariable (ScriptingContext context)
		{
			return var;
		}

		protected override bool DoAssign (ScriptingContext context, TargetObject obj)
		{
			if (!var.CanWrite)
				return false;

			TargetObject new_obj = Convert.ImplicitConversionRequired (
				context, obj, var.Type);

			var.SetObject (context.CurrentFrame.Frame, new_obj);
			return true;
		}
	}

	public class SourceExpression : Expression
	{
		SourceLocation location;

		public SourceExpression (SourceLocation location)
		{
			this.location = location;
			resolved = true;
		}

		public override string Name {
			get { return location.Name; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			resolved = true;
			return this;
		}

		protected override SourceLocation DoEvaluateSource (ScriptingContext context,
								    LocationType type, Expression[] types)
		{
			if (types != null)
				return null;

			return location;
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

                public static string MakeFQN (string nsn, string name)
                {
                        if (nsn == "")
                                return name;
                        return String.Concat (nsn, ".", name);
                }

		MemberExpression LookupMember (ScriptingContext context, FrameHandle frame,
					       string full_name)
		{
			Method method = frame.Frame.Method;
			if ((method == null) || (method.DeclaringType == null))
				return null;

			TargetClassObject instance = null;
			if (method.HasThis)
				instance = (TargetClassObject) method.This.GetObject (frame.Frame);

			TargetAccess target = frame.Frame.TargetAccess;
			MemberExpression member = StructAccessExpression.FindMember (
				target, method.DeclaringType, instance, full_name, true, true);
			if (member == null)
				return null;

			if (member.IsInstance && !method.HasThis)
				throw new ScriptingException (
					"Cannot use instance member `{0}' or current class " +
					"in static context.", full_name);

			return member;
		}

		Expression Lookup (ScriptingContext context, FrameHandle frame)
		{
			string[] namespaces = context.GetNamespaces (frame);
			if (namespaces == null)
				return null;

			foreach (string ns in namespaces) {
				string full_name = MakeFQN (ns, name);
				Expression expr = LookupMember (context, frame, full_name);
				if (expr != null)
					return expr;
			}

			return null;
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			if (frame.Frame.Method != null) {
				TargetVariable var = frame.Frame.Method.GetVariableByName (name);
				if (var != null)
					return new VariableAccessExpression (var);
			}

			Expression expr = LookupMember (context, frame, name);
			if (expr != null)
				return expr;

			expr = Lookup (context, frame);
			if (expr != null)
				return expr;

			SourceLocation location = context.FindMethod (name);
			if (location != null)
				return new SourceExpression (location);

			expr = DoResolveType (context);
			if (expr != null)
				return expr;

			throw new ScriptingException ("No symbol `{0}' in current context.", Name);
		}

		protected override Expression DoResolveType (ScriptingContext context)
		{
			Language language = context.CurrentLanguage;
			TargetType type = language.LookupType (name);
			if (type != null)
				return new TypeExpression (type);

			string[] namespaces = context.GetNamespaces (context.CurrentFrame);
			if (namespaces == null)
				return null;

			foreach (string ns in namespaces) {
				string full_name = MakeFQN (ns, name);
				type = language.LookupType (full_name);
				if (type != null)
					return new TypeExpression (type);
			}

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

		public MemberExpression ResolveMemberAccess (ScriptingContext context,
							     bool allow_instance)
		{
			MemberExpression member;

			TargetAccess target = context.CurrentFrame.TargetAccess;

			Expression lexpr = left.TryResolve (context);
			if (lexpr is TypeExpression) {
				TargetClassType stype = Convert.ToClassType (
					lexpr.EvaluateType (context));

				member = StructAccessExpression.FindMember (
					target, stype, null, name, true, true);
				if (member == null)
					throw new ScriptingException (
						"Type `{0}' has no member `{1}'",
						stype.Name, name);

				if (!member.IsStatic && !allow_instance)
					throw new ScriptingException (
						"Cannot access instance member `{0}' with a type " +
						"reference.", Name);

				return member;
			}

			if (lexpr != null) {
				TargetClassObject sobj = Convert.ToClassObject (
					target, lexpr.EvaluateObject (context));
				if (sobj == null)
					throw new ScriptingException (
						"`{0}' is not a struct or class", left.Name);

				member = StructAccessExpression.FindMember (
					target, sobj.Type, sobj, name, true, true);
				if (member == null)
					throw new ScriptingException (
						"Type `{0}' has no member `{1}'",
						sobj.Type.Name, name);

				if (!member.IsInstance)
					throw new ScriptingException (
						"Cannot access static member `{0}.{1}' with an " +
						"instance reference; use a type name instead.",
						sobj.Type.Name, name);

				return member;
			}

			Expression ltype = left.TryResolveType (context);
			if (ltype != null) {
				TargetClassType stype = Convert.ToClassType (
					ltype.EvaluateType (context));

				member = StructAccessExpression.FindMember (
					target, stype, null, name, true, true);
				if (member == null)
					throw new ScriptingException (
						"Type `{0}' has no member `{1}'",
						stype.Name, name);

				if (!member.IsStatic && !allow_instance)
					throw new ScriptingException (
						"Cannot access instance member `{0}' with a type " +
						"reference.", Name);

				return member;
			}

			throw new ScriptingException (
				"No such variable or type: `{0}'", left.Name);
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return ResolveMemberAccess (context, false);
		}

		protected override Expression DoResolveMethod (ScriptingContext context)
		{
			return ResolveMemberAccess (context, true);
		}

		protected override Expression DoResolveType (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;

			TargetType the_type;

			Expression ltype = left.TryResolveType (context);
			if (ltype == null)
				the_type = frame.Language.LookupType (Name);
			else {
				string nested = ltype.Name + "+" + name;
				the_type = frame.Language.LookupType (nested);
			}

			if (the_type == null)
				return null;

			return new TypeExpression (the_type);
		}
	}

	public abstract class MemberExpression : Expression
	{
		public abstract TargetClassObject InstanceObject {
			get;
		}

		public abstract bool IsInstance {
			get;
		}

		public abstract bool IsStatic {
			get;
		}
	}

	public class MethodGroupExpression : MemberExpression
	{
		protected readonly TargetClassType stype;
		protected readonly TargetClassObject instance;
		protected readonly string name;
		protected readonly TargetFunctionType[] methods;
		protected readonly bool is_instance, is_static;

		public MethodGroupExpression (TargetClassType stype, TargetClassObject instance,
					      string name, TargetFunctionType[] methods,
					      bool is_instance, bool is_static)
		{
			this.stype = stype;
			this.instance = instance;
			this.name = name;
			this.methods = methods;
			this.is_instance = is_instance;
			this.is_static = is_static;
			resolved = true;
		}

		public override string Name {
			get { return stype.Name + "." + name; }
		}

		public override TargetClassObject InstanceObject {
			get { return instance; }
		}

		public override bool IsInstance {
			get { return is_instance; }
		}

		public override bool IsStatic {
			get { return is_static; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return this;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			throw new ScriptingException ("Expression `{0}' is a method, not a " +
						      "field or property.", Name);
		}

		protected override TargetFunctionType DoEvaluateMethod (ScriptingContext context,
									LocationType type,
									Expression[] types)
		{
			if (type != LocationType.Method)
				return null;

			if (types == null) {
				if (methods.Length == 1)
					return (TargetFunctionType) methods [0];

                                throw new ScriptingException (
                                        "Ambiguous method `{0}'; need to use full name", Name);
			}

			TargetType[] argtypes = new TargetType [types.Length];
			for (int i = 0; i < types.Length; i++)
				argtypes [i] = types [i].EvaluateType (context);

			TargetFunctionType func = OverloadResolve (context, argtypes);
			if (func != null)
				return func;

			ArrayList list = new ArrayList ();
			foreach (TargetFunctionType method in methods) {
				if (method.Source == null)
					continue;
				list.Add (method.Source);
			}
			if (list.Count == 0)
				return null;
			SourceMethod[] sources = new SourceMethod [list.Count];
			list.CopyTo (sources, 0);
			throw new MultipleLocationsMatchException (sources);
		}

		public TargetFunctionType OverloadResolve (ScriptingContext context,
							   TargetType[] argtypes)
		{
			ArrayList candidates = new ArrayList ();

			foreach (TargetFunctionType method in methods) {
				if (method.ParameterTypes.Length != argtypes.Length)
					continue;

				candidates.Add (method);
			}

			TargetFunctionType candidate;
			if (candidates.Count == 1) {
				candidate = (TargetFunctionType) candidates [0];
				string error;
				if (IsApplicable (context, candidate, argtypes, out error))
					return candidate;

				throw new ScriptingException (
					"The best overload of method `{0}' has some invalid " +
					"arguments:\n{1}", Name, error);
			}

			if (candidates.Count == 0)
				throw new ScriptingException (
					"No overload of method `{0}' has {1} arguments.",
					Name, argtypes.Length);

			candidate = OverloadResolve (context, argtypes, candidates);

			if (candidate == null)
				throw new ScriptingException (
					"Ambiguous method `{0}'; need to use " +
					"full name", Name);

			return candidate;
		}

		public static bool IsApplicable (ScriptingContext context, TargetFunctionType method,
						 TargetType[] types, out string error)
		{
			for (int i = 0; i < types.Length; i++) {
				TargetType param_type = method.ParameterTypes [i];

				if (param_type == types [i])
					continue;

				if (Convert.ImplicitConversionExists (context, types [i], param_type))
					continue;

				error = String.Format (
					"Argument {0}: Cannot implicitly convert `{1}' to `{2}'",
					i, types [i].Name, param_type.Name);
				return false;
			}

			error = null;
			return true;
		}

		static TargetFunctionType OverloadResolve (ScriptingContext context,
							   TargetType[] argtypes,
							   ArrayList candidates)
		{
			// Ok, no we need to find an exact match.
			TargetFunctionType match = null;
			foreach (TargetFunctionType method in candidates) {
				string error;
				if (!IsApplicable (context, method, argtypes, out error))
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

	public class TypeOfExpression : Expression
	{
		Expression expr;

		public TypeOfExpression (Expression expr)
		{
			this.expr = expr;
		}

		public override string Name {
			get { return String.Format ("typeof ({0})", expr.Name); }
		}

		protected override Expression DoResolveType (ScriptingContext context)
		{
			return expr.ResolveType (context);
		}
		
		protected override Expression DoResolve (ScriptingContext context)
		{
			return expr.Resolve (context);
		}
	}

	public abstract class PointerExpression : Expression
	{
		public abstract TargetAddress EvaluateAddress (ScriptingContext context);
	}

	public class RegisterExpression : PointerExpression
	{
		string name;
		int register = -1;

		public RegisterExpression (string register)
		{
			this.name = register;
			resolved = true;
		}

		public override string Name {
			get { return '%' + name; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return this;
		}

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			return context.CurrentLanguage.PointerType;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			register = context.CurrentProcess.GetRegisterIndex (name);
			try {
				long value = frame.Frame.GetRegister (register);
				TargetAddress address = new TargetAddress (
					context.AddressDomain, value);
				return context.CurrentLanguage.CreatePointer (frame.Frame, address);
			} catch {
				throw new ScriptingException (
					"Can't access register `{0}' selected stack frame.", name);
			}
		}

		public override TargetAddress EvaluateAddress (ScriptingContext context)
		{
			TargetPointerObject pobj = (TargetPointerObject) EvaluateObject (context);
			return pobj.Address;
		}

		protected override bool DoAssign (ScriptingContext context, TargetObject tobj)
		{
			TargetFundamentalObject fobj = tobj as TargetFundamentalObject;
			if (fobj == null)
				throw new ScriptingException (
					"Cannot store non-fundamental object `{0}' in " +
					" a registers", tobj);

			FrameHandle frame = context.CurrentFrame;
			object obj = fobj.GetObject (frame.Frame.TargetAccess);
			long value = System.Convert.ToInt64 (obj);
			register = context.CurrentProcess.GetRegisterIndex (name);
			frame.Frame.SetRegister (register, value);
			return true;
		}
	}

	public class StructAccessExpression : MemberExpression
	{
		public readonly TargetClassType Type;
		public readonly TargetMemberInfo Member;
		protected readonly TargetClassObject instance;

		protected StructAccessExpression (TargetClassType type,
						  TargetClassObject instance,
						  TargetMemberInfo member)
		{
			this.Type = type;
			this.Member = member;
			this.instance = instance;
			resolved = true;
		}

		public override string Name {
			get { return Member.Name; }
		}

		public override TargetClassObject InstanceObject {
			get { return instance; }
		}

		public override bool IsInstance {
			get { return !Member.IsStatic; }
		}

		public override bool IsStatic {
			get { return Member.IsStatic; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return this;
		}

		protected TargetObject GetField (TargetAccess target, TargetFieldInfo field)
		{
			if (field.IsStatic)
				return Type.GetStaticField (target, field);
			else
				return InstanceObject.GetField (target, field);
		}

		protected TargetObject GetProperty (ScriptingContext context,
						     TargetPropertyInfo prop)
		{
			string exc_message;
			TargetObject res = context.CurrentProcess.RuntimeInvoke (
				prop.Getter, InstanceObject, new TargetObject [0],
				out exc_message);

			if (exc_message != null)
				throw new ScriptingException (
					"Invocation of `{0}' raised an exception: {1}",
					Name, exc_message);

			return res;
		}

		protected TargetObject GetMember (ScriptingContext context, TargetAccess target,
						   TargetMemberInfo member)
		{
			if (member is TargetPropertyInfo)
				return GetProperty (context, (TargetPropertyInfo) member);
			else if (member is TargetFieldInfo)
				return GetField (target, (TargetFieldInfo) member);
			else
				throw new ScriptingException ("Member `{0}' is of unknown type {1}",
							      Name, member.GetType ());
		}

		public static MemberExpression FindMember (TargetAccess target, TargetClassType stype,
							   TargetClassObject instance, string name,
							   bool search_static, bool search_instance)
		{
		again:
			TargetMemberInfo member = stype.FindMember (
				name, search_static, search_instance);

			if (member != null)
				return new StructAccessExpression (stype, instance, member);

			ArrayList methods = new ArrayList ();
			bool is_instance = false;
			bool is_static = false;

			if (name == ".ctor") {
				foreach (TargetMethodInfo method in stype.Constructors) {
					methods.Add (method.Type);
					is_instance = true;
				}
			} else if (name == ".cctor") {
				foreach (TargetMethodInfo method in stype.StaticConstructors) {
					methods.Add (method.Type);
					is_static = true;
				}
			} else {
				if (search_instance) {
					foreach (TargetMethodInfo method in stype.Methods) {
						if (method.Name != name)
							continue;

						methods.Add (method.Type);
						is_instance = true;
					}
				}

				if (search_static) {
					foreach (TargetMethodInfo method in stype.StaticMethods) {
						if (method.Name != name)
							continue;

						methods.Add (method.Type);
						is_static = true;
					}
				}
			}

			if (methods.Count > 0) {
				TargetFunctionType[] funcs = new TargetFunctionType [methods.Count];
				methods.CopyTo (funcs, 0);
				return new MethodGroupExpression (
					stype, instance, name, funcs, is_instance, is_static);
			}

			TargetClassType ctype = stype as TargetClassType;
			if ((ctype != null) && ctype.HasParent) {
				stype = ctype.ParentType;
				goto again;
			}

			return null;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;

			if (!Member.IsStatic && (InstanceObject == null))
				throw new ScriptingException (
					"Instance member `{0}' cannot be used in static context.", Name);

			try {
				return GetMember (context, frame.TargetAccess, Member);
			} catch (TargetException ex) {
				throw new ScriptingException ("Cannot access struct member `{0}': {1}",
							      Name, ex.Message);
			}
		}

		public TargetFunctionType ResolveDelegate (ScriptingContext context)
		{
			MethodGroupExpression mg = InvocationExpression.ResolveDelegate (
				context, this);
			if (mg == null)
				return null;

			return mg.OverloadResolve (context, null);
		}

		protected override TargetFunctionType DoEvaluateMethod (ScriptingContext context,
									 LocationType type,
									 Expression[] types)
		{
			switch (type) {
			case LocationType.PropertyGetter:
			case LocationType.PropertySetter:
				TargetPropertyInfo property = Member as TargetPropertyInfo;
				if (property == null)
					return null;

				if (type == LocationType.PropertyGetter) {
					if (!property.CanRead)
						throw new ScriptingException (
							"Property {0} doesn't have a getter.", Name);
					return property.Getter;
				} else {
					if (!property.CanWrite)
						throw new ScriptingException (
							"Property {0} doesn't have a setter.", Name);
					return property.Setter;
				}

			case LocationType.EventAdd:
			case LocationType.EventRemove:
				TargetEventInfo ev = Member as TargetEventInfo;
				if (ev == null)
					return null;

				if (type == LocationType.EventAdd)
					return ev.Add;
				else
					return ev.Remove;

			case LocationType.Method:
			case LocationType.DelegateInvoke:
				MethodGroupExpression mg = InvocationExpression.ResolveDelegate (
					context, this);
				if (mg == null)
					return null;

				return mg.EvaluateMethod (context, LocationType.Method, types);

			default:
				return null;
			}
		}

		protected void SetField (TargetAccess target, TargetFieldInfo field, TargetObject obj)
		{
			if (field.IsStatic)
				Type.SetStaticField (target, field, obj);
			else
				InstanceObject.SetField (target, field, obj);
		}

		protected override bool DoAssign (ScriptingContext context, TargetObject obj)
		{
			if (Member is TargetFieldInfo) {
				StackFrame frame = context.CurrentFrame.Frame;

				if (Member.Type != obj.Type)
					throw new ScriptingException (
						"Type mismatch: cannot assign expression of type " +
						"`{0}' to field `{1}', which is of type `{2}'.",
						obj.TypeName, Name, Member.Type.Name);

				SetField (frame.TargetAccess, (TargetFieldInfo) Member, obj);
			}
			else if (Member is TargetPropertyInfo) 
			  	throw new ScriptingException ("Can't set properties directly.");
			else if (Member is TargetEventInfo)
				throw new ScriptingException ("Can't set events directly.");
			else if (Member is TargetMethodInfo)
				throw new ScriptingException ("Can't set methods directly.");

			return true;
		}
	}

	public class PointerDereferenceExpression : PointerExpression
	{
		Expression expr;
		string name;
		bool current_ok;

		public PointerDereferenceExpression (Expression expr, bool current_ok)
		{
			this.expr = expr;
			this.current_ok = current_ok;
			name = '*' + expr.Name;
		}

		public override string Name {
			get {
				return name;
			}
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			expr = expr.Resolve (context);
			if (expr == null)
				return null;

			resolved = true;
			return this;
		}

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			TargetType type = expr.EvaluateType (context);

			TargetPointerType ptype = type as TargetPointerType;
			if (ptype != null)
				return ptype.StaticType;

			throw new ScriptingException (
				"Expression `{0}' is not a pointer.", expr.Name);
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			TargetObject obj = expr.EvaluateObject (context);

			TargetAccess target = context.CurrentFrame.TargetAccess;
			TargetPointerObject pobj = obj as TargetPointerObject;
			if (pobj != null) {
				TargetObject result;
				try {
					result = pobj.GetDereferencedObject (target);
				} catch {
					result = null;
				}

				if (result == null)
					throw new ScriptingException (
						"Cannot dereference `{0}'.", expr.Name);

				return result;
			}

			TargetClassObject cobj = obj as TargetClassObject;
			if (current_ok && (cobj != null))
				return cobj;

			throw new ScriptingException (
				"Expression `{0}' is not a pointer type.", expr.Name);
		}

		public override TargetAddress EvaluateAddress (ScriptingContext context)
		{
			object obj = expr.Resolve (context);
			if (obj is int)
				obj = (long) (int) obj;
			if (obj is long)
				return new TargetAddress (context.AddressDomain, (long) obj);

			TargetPointerObject pobj = obj as TargetPointerObject;
			if (pobj == null)
				throw new ScriptingException (
					"Expression `{0}' is not a pointer type.", expr.Name);

			return pobj.Address;
		}
	}

	public class AddressOfExpression : PointerExpression
	{
		Expression expr;
		string name;

		public AddressOfExpression (Expression expr)
		{
			this.expr = expr;
			name = '&' + expr.Name;
		}

		public override string Name {
			get {
				return name;
			}
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			expr = expr.Resolve (context);
			if (expr == null)
				return null;

			resolved = true;
			return this;
		}

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			TargetPointerType ptype = expr.EvaluateType (context)
				as TargetPointerType;
			if (ptype != null)
				return ptype;

			return context.CurrentLanguage.PointerType;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;

			TargetAddress address = EvaluateAddress (context);

			return context.CurrentLanguage.CreatePointer (frame, address);
		}

		public override TargetAddress EvaluateAddress (ScriptingContext context)
		{
			PointerExpression pexpr = expr as PointerExpression;
			if (pexpr != null)
				return pexpr.EvaluateAddress (context);

			TargetObject obj = expr.EvaluateObject (context);
			if ((obj == null) || !obj.HasAddress)
				throw new ScriptingException (
					"Cannot take address of expression `{0}'", expr.Name);

			return obj.Address;
		}
	}

	public class ArrayAccessExpression : Expression
	{
		Expression expr;
		Expression[] indices;
		string name;

		public ArrayAccessExpression (Expression expr, Expression[] indices)
		{
			this.expr = expr;
			this.indices = indices;

			StringBuilder sb = new StringBuilder("");
			bool comma = false;
			foreach (Expression index in indices) {
				if (comma) sb.Append(",");
				sb.Append (index.ToString());
				comma = true;
			}
			name = String.Format ("{0}[{1}]", expr.Name, sb.ToString());
		}

		public override string Name {
			get {
				return name;
			}
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			int i;
			expr = expr.Resolve (context);
			if (expr == null)
				return null;

			for (i = 0; i < indices.Length; i ++) {
				indices[i] = indices[i].Resolve (context);
				if (indices[i] == null)
					return null;
			}

			resolved = true;
			return this;
		}

		int GetIntIndex (TargetAccess target, Expression index, ScriptingContext context)
		{
			try {
				object idx = index.Evaluate (context);

				if (idx is int)
					return (int) idx;

				TargetFundamentalObject obj = (TargetFundamentalObject) idx;
				return (int) obj.GetObject (target);
			} catch (Exception e) {
				throw new ScriptingException (
					"Cannot convert {0} to an integer for indexing: {1}",
					index, e);
			}
		}

		int[] GetIntIndices (TargetAccess target, ScriptingContext context)
		{
			int[] int_indices = new int [indices.Length];
			for (int i = 0; i < indices.Length; i++)
				int_indices [i] = GetIntIndex (target, indices [i], context);
			return int_indices;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			TargetAccess target = context.CurrentFrame.TargetAccess;
			TargetObject obj = expr.EvaluateObject (context);

			// array[int]
			TargetArrayObject aobj = obj as TargetArrayObject;
			if (aobj != null) {
				int[] int_indices = GetIntIndices (target, context);
				try {
					return aobj.GetElement (target, int_indices);
				} catch (ArgumentException ex) {
					throw new ScriptingException (
						"Index of array expression `{0}' out of bounds.",
						expr.Name);
				}
			}

			// pointer[int]
			TargetPointerObject pobj = obj as TargetPointerObject;
			if (pobj != null) {
				// single dimensional array only at present
				int[] int_indices = GetIntIndices (target, context);
				if (int_indices.Length != 1)
					throw new ScriptingException (
						"Multi-dimensial arrays of type {0} are not yet supported",
						expr.Name);

				if (pobj.Type.IsArray)
					return pobj.GetArrayElement (target, int_indices [0]);

				throw new ScriptingException (
						       "Variable {0} is not an array type.", expr.Name);
			}

			// indexers
			TargetClassObject sobj = Convert.ToClassObject (target, obj);
			if (sobj != null) {
				ArrayList props = new ArrayList ();
				foreach (TargetPropertyInfo prop in sobj.Type.Properties) {
					if (!prop.CanRead)
						continue;

					props.Add (prop.Getter);
				}

				if (props.Count == 0)
					throw new ScriptingException (
						"Indexer `{0}' doesn't have a getter.", expr.Name);

				TargetFunctionType[] funcs = new TargetFunctionType [props.Count];
				props.CopyTo (funcs, 0);

				MethodGroupExpression mg = new MethodGroupExpression (
					sobj.Type, sobj, expr.Name + ".this", funcs, true, false);

				InvocationExpression invocation = new InvocationExpression (
					mg, indices);
				invocation.Resolve (context);

				return invocation.EvaluateObject (context);
			}

			throw new ScriptingException (
				"{0} is neither an array/pointer type, nor is it " +
				"an object with a valid indexer.", expr);
		}

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			TargetArrayType type = expr.EvaluateType (context)
				as TargetArrayType;
			if (type == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", expr.Name);

			return type.ElementType;
		}

		protected override bool DoAssign (ScriptingContext context, TargetObject right)
		{
			TargetAccess target = context.CurrentFrame.TargetAccess;
			TargetObject obj = expr.EvaluateObject (context);

			// array[int]
			TargetArrayObject aobj = obj as TargetArrayObject;
			if (aobj != null) {
				int[] int_indices = GetIntIndices (target, context);
				try {
					aobj.SetElement (target, int_indices, right);
				} catch (ArgumentException ex) {
					throw new ScriptingException (
						"Index of array expression `{0}' out of bounds.",
						expr.Name);
				}

				return true;
			}

			// indexers
			TargetClassObject sobj = Convert.ToClassObject (target, obj);
			if (sobj != null) {
				ArrayList props = new ArrayList ();
				foreach (TargetPropertyInfo prop in sobj.Type.Properties) {
					if (!prop.CanWrite)
						continue;

					props.Add (prop.Setter);
				}

				if (props.Count == 0)
					throw new ScriptingException (
						"Indexer `{0}' doesn't have a setter.", expr.Name);

				TargetFunctionType[] funcs = new TargetFunctionType [props.Count];
				props.CopyTo (funcs, 0);

				MethodGroupExpression mg = new MethodGroupExpression (
					sobj.Type, sobj, expr.Name + "[]", funcs, true, false);

				Expression[] indexargs = new Expression [indices.Length + 1];
				indices.CopyTo (indexargs, 0);
				indexargs [indices.Length] = new ArgumentExpression (right);

				InvocationExpression invocation = new InvocationExpression (
					mg, indexargs);
				invocation.Resolve (context);

				invocation.Invoke (context, false);
				return true;
			}
			
			throw new ScriptingException (
				"{0} is neither an array/pointer type, nor is it " +
				"an object with a valid indexer.", expr);
		}

	}

	public class CastExpression : Expression
	{
		Expression target, expr;
		string name;

		public CastExpression (Expression target, Expression expr)
		{
			this.target = target;
			this.expr = expr;
			this.name = String.Format ("(({0}) {1})", target.Name, expr.Name);
		}

		public override string Name {
			get {
				return name;
			}
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			target = target.ResolveType (context);
			if (target == null)
				return null;

			expr = expr.Resolve (context);
			if (expr == null)
				return null;

			resolved = true;
			return this;
		}

		static TargetClassObject TryParentCast (ScriptingContext context,
							TargetAccess target,
							TargetClassObject source,
							TargetClassType source_type,
							TargetClassType target_type)
		{
			if (source_type == target_type)
				return source;

			if (!source_type.HasParent)
				return null;

			source = TryParentCast (
				context, target, source, source_type.ParentType, target_type);
			if (source == null)
				return null;

			return source.GetParentObject (target);
		}

		static TargetClassObject TryCurrentCast (ScriptingContext context,
							 TargetAccess target,
							 TargetClassObject source,
							 TargetClassType target_type)
		{
			TargetClassObject current = source.GetCurrentObject (target);
			if (current == null)
				return null;

			return TryParentCast (context, target, current, current.Type, target_type);
		}

		public static TargetObject TryCast (ScriptingContext context, TargetObject source,
						    TargetClassType target_type)
		{
			if (source.Type == target_type)
				return source;

			TargetAccess target = context.CurrentFrame.TargetAccess;

			TargetClassObject sobj = Convert.ToClassObject (target, source);
			if (sobj == null)
				return null;

			TargetClassObject result;
			result = TryParentCast (context, target, sobj, sobj.Type, target_type);
			if (result != null)
				return result;

			return TryCurrentCast (context, target, sobj, target_type);
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			TargetClassType type = Convert.ToClassType (target.EvaluateType (context));

			TargetClassObject source = Convert.ToClassObject (
				context.CurrentFrame.TargetAccess, expr.EvaluateObject (context));
			if (source == null)
				throw new ScriptingException (
					"Variable {0} is not a class type.", expr.Name);

			TargetObject obj = TryCast (context, source, type);
			if (obj == null)
				throw new ScriptingException (
					"Cannot cast from {0} to {1}.", source.Type.Name,
					type.Name);

			return obj;
		}

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			TargetObject obj = EvaluateObject (context);
			if (obj == null)
				return null;

			return obj.Type;
		}
	}

	public static class Convert
	{
		static bool ImplicitFundamentalConversionExists (FundamentalKind skind,
								 FundamentalKind tkind)
		{
			//
			// See Convert.ImplicitStandardConversionExists in MCS.
			//
			switch (skind) {
			case FundamentalKind.SByte:
				if ((tkind == FundamentalKind.Int16) ||
				    (tkind == FundamentalKind.Int32) ||
				    (tkind == FundamentalKind.Int64) ||
				    (tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.Byte:
				if ((tkind == FundamentalKind.Int16) ||
				    (tkind == FundamentalKind.UInt16) ||
				    (tkind == FundamentalKind.Int32) ||
				    (tkind == FundamentalKind.UInt32) ||
				    (tkind == FundamentalKind.Int64) ||
				    (tkind == FundamentalKind.UInt64) ||
				    (tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.Int16:
				if ((tkind == FundamentalKind.Int32) ||
				    (tkind == FundamentalKind.Int64) ||
				    (tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.UInt16:
				if ((tkind == FundamentalKind.Int32) ||
				    (tkind == FundamentalKind.UInt32) ||
				    (tkind == FundamentalKind.Int64) ||
				    (tkind == FundamentalKind.UInt64) ||
				    (tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.Int32:
				if ((tkind == FundamentalKind.Int64) ||
				    (tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.UInt32:
				if ((tkind == FundamentalKind.Int64) ||
				    (tkind == FundamentalKind.UInt64) ||
				    (tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.Int64:
			case FundamentalKind.UInt64:
				if ((tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.Char:
				if ((tkind == FundamentalKind.UInt16) ||
				    (tkind == FundamentalKind.Int32) ||
				    (tkind == FundamentalKind.UInt32) ||
				    (tkind == FundamentalKind.Int64) ||
				    (tkind == FundamentalKind.UInt64) ||
				    (tkind == FundamentalKind.Single) ||
				    (tkind == FundamentalKind.Double))
					return true;
				break;

			case FundamentalKind.Single:
				if (tkind == FundamentalKind.Double)
					return true;
				break;

			default:
				break;
			}

			return false;
		}

		static bool ImplicitFundamentalConversionExists (ScriptingContext context,
								 TargetFundamentalType source,
								 TargetFundamentalType target)
		{
			return ImplicitFundamentalConversionExists (
				source.FundamentalKind, target.FundamentalKind);
		}

		static object ImplicitFundamentalConversion (object value, FundamentalKind tkind)
		{
			switch (tkind) {
			case FundamentalKind.Char:
				return System.Convert.ToChar (value);
			case FundamentalKind.SByte:
				return System.Convert.ToSByte (value);
			case FundamentalKind.Byte:
				return System.Convert.ToByte (value);
			case FundamentalKind.Int16:
				return System.Convert.ToInt16 (value);
			case FundamentalKind.UInt16:
				return System.Convert.ToUInt16 (value);
			case FundamentalKind.Int32:
				return System.Convert.ToInt32 (value);
			case FundamentalKind.UInt32:
				return System.Convert.ToUInt32 (value);
			case FundamentalKind.Int64:
				return System.Convert.ToInt64 (value);
			case FundamentalKind.UInt64:
				return System.Convert.ToUInt64 (value);
			case FundamentalKind.Single:
				return System.Convert.ToSingle (value);
			case FundamentalKind.Double:
				return System.Convert.ToDouble (value);
			default:
				return null;
			}
		}

		static TargetObject ImplicitFundamentalConversion (ScriptingContext context,
								   TargetFundamentalObject obj,
								   TargetFundamentalType type)
		{
			FundamentalKind skind = obj.Type.FundamentalKind;
			FundamentalKind tkind = type.FundamentalKind;

			if (!ImplicitFundamentalConversionExists (skind, tkind))
				return null;

			TargetAccess target = context.CurrentFrame.TargetAccess;
			object value = obj.GetObject (target);

			object new_value = ImplicitFundamentalConversion (value, tkind);
			if (new_value == null)
				return null;

			return type.Language.CreateInstance (target, new_value);
		}

		static bool ImplicitReferenceConversionExists (ScriptingContext context,
							       TargetClassType source,
							       TargetClassType target)
		{
			if (source == target)
				return true;

			if (!source.HasParent)
				return false;

			return ImplicitReferenceConversionExists (
				context, source.ParentType, target);
		}

		static TargetObject ImplicitReferenceConversion (ScriptingContext context,
								 TargetClassObject obj,
								 TargetClassType type)
		{
			if (obj.Type == type)
				return obj;

			if (!obj.Type.HasParent)
				return null;

			return obj.GetParentObject (context.CurrentFrame.TargetAccess);
		}

		public static bool ImplicitConversionExists (ScriptingContext context,
							     TargetType source, TargetType target)
		{
			if (source.Equals (target))
				return true;

			if ((source is TargetFundamentalType) && (target is TargetFundamentalType))
				return ImplicitFundamentalConversionExists (
					context, (TargetFundamentalType) source,
					(TargetFundamentalType) target);

			if ((source is TargetClassType) && (target is TargetClassType))
				return ImplicitReferenceConversionExists (
					context, (TargetClassType) source,
					(TargetClassType) target);

			return false;
		}

		public static TargetObject ImplicitConversion (ScriptingContext context,
							       TargetObject obj, TargetType type)
		{
			if (obj.Type.Equals (type))
				return obj;

			if ((obj is TargetFundamentalObject) && (type is TargetFundamentalType))
				return ImplicitFundamentalConversion (
					context, (TargetFundamentalObject) obj,
					(TargetFundamentalType) type);

			if ((obj is TargetClassObject) && (type is TargetClassType))
				return ImplicitReferenceConversion (
					context, (TargetClassObject) obj,
					(TargetClassType) type);

			return null;
		}

		public static TargetObject ImplicitConversionRequired (ScriptingContext context,
								       TargetObject obj, TargetType type)
		{
			TargetObject new_obj = ImplicitConversion (context, obj, type);
			if (new_obj != null)
				return new_obj;

			throw new ScriptingException (
				"Cannot implicitly convert `{0}' to `{1}'", obj.Type.Name, type.Name);
		}

		public static TargetClassType ToClassType (TargetType type)
		{
			TargetClassType ctype = type as TargetClassType;
			if (ctype != null)
				return ctype;

			TargetObjectType otype = type as TargetObjectType;
			if (otype != null) {
				ctype = otype.ClassType;
				if (ctype != null)
					return ctype;
			}

			throw new ScriptingException (
				"Type `{0}' is not a struct or class.", type.Name);
		}

		public static TargetClassObject ToClassObject (TargetAccess target, TargetObject obj)
		{
			TargetClassObject cobj = obj as TargetClassObject;
			if (cobj != null)
				return cobj;

			TargetObjectObject oobj = obj as TargetObjectObject;
			if (oobj != null)
				return oobj.GetClassObject (target);

			return null;
		}
	}

	public class ConditionalExpression : Expression
	{
		Expression test;
		Expression true_expr;
		Expression false_expr;

		public override string Name {
			get { return "conditional"; }
		}

		public ConditionalExpression (Expression test, Expression true_expr, Expression false_expr)
		{
		  this.test = test;
		  this.true_expr = true_expr;
		  this.false_expr = false_expr;
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
		  this.test = this.test.Resolve (context);
		  if (this.test == null)
		    return null;

		  this.true_expr = this.true_expr.Resolve (context);
		  if (this.true_expr == null)
		    return null;

		  this.false_expr = this.false_expr.Resolve (context);
		  if (this.false_expr == null)
		    return null;

			resolved = true;
			return this;
		}

		protected override object DoEvaluate (ScriptingContext context)
		{
			bool cond;

			try {
				cond = (bool) this.test.Evaluate (context);
			}
			catch (Exception e) {
				throw new ScriptingException (
					"Cannot convert {0} to a boolean for conditional: {1}",
					this.test, e);
			}

			return cond ? true_expr.Evaluate (context) : false_expr.Evaluate (context);
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			bool cond;

			try {
				cond = (bool) this.test.Evaluate (context);
			}
			catch (Exception e) {
				throw new ScriptingException (
					"Cannot convert {0} to a boolean for conditional: {1}",
					this.test, e);
			}

			return cond ? true_expr.EvaluateObject (context) : false_expr.EvaluateObject (context);
		}
	}

	public class InvocationExpression : Expression
	{
		Expression method_expr;
		Expression[] arguments;
		MethodGroupExpression mg;
		string name;

		TargetType[] argtypes;
		TargetFunctionType method;

		public InvocationExpression (Expression method_expr, Expression[] arguments)
		{
			this.method_expr = method_expr;
			this.arguments = arguments;

			name = String.Format ("{0} ()", method_expr.Name);
		}

		public override string Name {
			get { return name; }
		}

		public static MethodGroupExpression ResolveDelegate (ScriptingContext context,
								     Expression expr)
		{
			TargetClassObject sobj = Convert.ToClassObject (
				context.CurrentFrame.TargetAccess, expr.EvaluateObject (context));
			if (sobj == null)
				return null;

			TargetClassType delegate_type = sobj.Type.Language.DelegateType;
			if (CastExpression.TryCast (context, sobj, delegate_type) == null)
				return null;

			TargetFunctionType invoke = null;
			foreach (TargetMethodInfo method in sobj.Type.Methods) {
				if (method.Name == "Invoke") {
					invoke = method.Type;
					break;
				}
			}

			if (invoke == null)
				return null;

			TargetFunctionType[] methods = new TargetFunctionType[] { invoke };

			MethodGroupExpression mg = new MethodGroupExpression (
				sobj.Type, sobj, "Invoke", methods, true, false);
			return mg;
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			method_expr = method_expr.Resolve (context);
			if (method_expr == null)
				return null;

			mg = method_expr as MethodGroupExpression;
			if (mg == null)
				mg = ResolveDelegate (context, method_expr);

			if (mg == null)
				throw new ScriptingException (
					"Expression `{0}' is not a method.", Name);

			argtypes = new TargetType [arguments.Length];

			for (int i = 0; i < arguments.Length; i++) {
				arguments [i] = arguments [i].Resolve (context);
				if (arguments [i] == null)
					return null;

				argtypes [i] = arguments [i].EvaluateType (context);
			}

			method = mg.OverloadResolve (context, argtypes);

			resolved = true;
			return this;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			TargetObject retval = DoInvoke (context, false);

			if (!method.HasReturnValue)
				throw new ScriptingException (
					"Method `{0}' doesn't return a value.", Name);

			return retval;
		}

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			return method.ReturnType;
		}

		protected override TargetFunctionType DoEvaluateMethod (ScriptingContext context,
									 LocationType type,
									 Expression[] types)
		{
			return method_expr.EvaluateMethod (context, type, types);
		}

		protected TargetObject DoInvoke (ScriptingContext context, bool debug)
		{
			TargetObject[] args = new TargetObject [arguments.Length];

			for (int i = 0; i < arguments.Length; i++)
				args [i] = arguments [i].EvaluateObject (context);

			TargetObject[] objs = new TargetObject [args.Length];
			for (int i = 0; i < args.Length; i++) {
				objs [i] = Convert.ImplicitConversionRequired (
					context, args [i], method.ParameterTypes [i]);
			}

			TargetClassObject instance = mg.InstanceObject;

			try {
				if (debug) {
					context.CurrentProcess.RuntimeInvoke (method, instance, objs);
					return null;
				}

				string exc_message;
				TargetObject retval = context.CurrentProcess.RuntimeInvoke (
					method, mg.InstanceObject, objs, out exc_message);

				if (exc_message != null)
					throw new ScriptingException (
						"Invocation of `{0}' raised an exception: {1}",
						Name, exc_message);

				return retval;
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Invocation of `{0}' raised an exception: {1}", Name, ex.Message);
			}
		}

		public void Invoke (ScriptingContext context, bool debug)
		{
			DoInvoke (context, debug);
		}
	}

	public class NewExpression : Expression
	{
		Expression type_expr;
		Expression[] arguments;
		string name;

		public NewExpression (Expression type_expr, Expression[] arguments)
		{
			this.type_expr = type_expr;
			this.arguments = arguments;

			name = String.Format ("new {0} ()", type_expr.Name);
		}

		public override string Name {
			get { return name; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			type_expr = type_expr.ResolveType (context);
			if (type_expr == null)
				return null;

			for (int i = 0; i < arguments.Length; i++) {
				arguments [i] = arguments [i].Resolve (context);
				if (arguments [i] == null)
					return null;
			}

			resolved = true;
			return this;
		}

		protected override TargetType DoEvaluateType (ScriptingContext context)
		{
			return type_expr.EvaluateType (context);
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			return Invoke (context);
		}

		public TargetObject Invoke (ScriptingContext context)
		{
			TargetClassType stype = Convert.ToClassType (
				type_expr.EvaluateType (context));

			TargetMethodInfo[] ctors = stype.Constructors;
			TargetFunctionType[] funcs = new TargetFunctionType [ctors.Length];
			for (int i = 0; i < ctors.Length; i++)
				funcs [i] = ctors [i].Type;

			MethodGroupExpression mg = new MethodGroupExpression (
				stype, null, ".ctor", funcs, false, true);

			InvocationExpression invocation = new InvocationExpression (mg, arguments);
			invocation.Resolve (context);

			return invocation.EvaluateObject (context);
		}
	}

	public class AssignmentExpression : Expression
	{
		Expression left, right;
		string name;

		public AssignmentExpression (Expression left, Expression right)
		{
			this.left = left;
			this.right = right;

			name = left.Name + "=" + right.Name;
		}

		public override string Name {
			get { return name; }
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			left = left.Resolve (context);
			if (left == null)
				return null;

			right = right.Resolve (context);
			if (right == null)
				return null;

			resolved = true;
			return this;
		}

		protected override TargetObject DoEvaluateObject (ScriptingContext context)
		{
			TargetObject obj;
			if (right is NullExpression) {
				StackFrame frame = context.CurrentFrame.Frame;
				TargetType ltype = left.EvaluateType (context);
				obj = frame.Language.CreateNullObject (frame.TargetAccess, ltype);
			} else
				obj = right.EvaluateObject (context);

			left.Assign (context, obj);
			return obj;
		}
	}
}
