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
						"unresolved expression `{0}' ({1})", Name,
						GetType ()));

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

		public SourceLocation EvaluateLocation (ScriptingContext context, LocationType type,
							Expression [] types)
		{
			if (!resolved)
				throw new InvalidOperationException (
					String.Format (
						"Some clown tried to evaluate the " +
						"unresolved expression `{0}'", Name));

			try {
				SourceLocation location = DoEvaluateLocation (context, type, types);
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (val.GetType ()))
				throw new ScriptingException ("Cannot instantiate value '{0}' in the current frame's language", Name);

			return frame.Language.CreateInstance (frame, val);
		}

		public override TargetAddress EvaluateAddress (ScriptingContext context)
		{
			return new TargetAddress (context.GlobalAddressDomain, Value);
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (typeof (string)))
				throw new ScriptingException ("Cannot instantiate value '{0}' in the current frame's language", Name);

			return frame.Language.CreateInstance (frame, val);
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;
			if ((frame.Language == null) ||
			    !frame.Language.CanCreateInstance (typeof (bool)))
				throw new ScriptingException ("Cannot instantiate value '{0}' in the current frame's language", Name);

			return frame.Language.CreateInstance (frame, val);
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			throw new InvalidOperationException ();
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
		protected IVariable var;

		protected override Expression DoResolve (ScriptingContext context)
		{
			frame = context.CurrentFrame;
			IMethod method = frame.Frame.Method;
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return (ITargetObject) frame.GetVariable (var);
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
			if (!((ITargetClassType) var.Type).HasParent)
				throw new ScriptingException (
					"Current class has no base class.");

			return expr;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return ((ITargetClassObject) base.DoEvaluateVariable (context)).Parent;
		}
	}

	public class CatchExpression : Expression
	{
		public override string Name {
			get { return "catch"; }
		}

		ITargetObject exc;

		protected override Expression DoResolve (ScriptingContext context)
		{
			exc = context.CurrentFrame.ExceptionObject;
			if (exc == null)
				throw new ScriptingException ("No current exception.");

			resolved = true;
			return this;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return exc;
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
					obj.TypeName, Name, var.Type.Name);

			var.SetObject (context.CurrentFrame.Frame, obj);
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

		protected override SourceLocation DoEvaluateLocation (ScriptingContext context,
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
			IMethod method = frame.Frame.Method;
			if ((method == null) || (method.DeclaringType == null))
				return null;

			ITargetStructObject instance = null;
			if (method.HasThis)
				instance = (ITargetStructObject) frame.GetVariable (method.This);

			MemberExpression member = StructAccessExpression.FindMember (
				method.DeclaringType, instance, full_name, true, true);
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
			IVariable var = frame.GetVariableInfo (name, false);
			if (var != null)
				return new VariableAccessExpression (var);

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
			FrameHandle frame = context.CurrentFrame;
			ITargetType type = frame.Language.LookupType (frame.Frame, name);
			if (type != null)
				return new TypeExpression (type);

			string[] namespaces = context.GetNamespaces (frame);
			if (namespaces == null)
				return null;

			foreach (string ns in namespaces) {
				string full_name = MakeFQN (ns, name);
				type = frame.Language.LookupType (frame.Frame, full_name);
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

		public MemberExpression ResolveMemberAccess (ScriptingContext context)
		{
			MemberExpression member;

			Expression lexpr = left.TryResolve (context);
			if (lexpr is TypeExpression) {
				ITargetStructType stype = lexpr.EvaluateType (context)
					as ITargetStructType;
				if (stype == null)
					throw new ScriptingException (
						"`{0}' is not a struct or class", lexpr.Name);

				member = StructAccessExpression.FindMember (
					stype, null, name, true, true);
				if (member == null)
					throw new ScriptingException (
						"Type `{0}' has no member `{1}'",
						stype.Name, name);

				if (!member.IsStatic)
					throw new ScriptingException (
						"Cannot access instance member `{0}' with a type " +
						"reference.", Name);

				return member;
			}

			if (lexpr != null) {
				ITargetStructObject sobj = lexpr.EvaluateVariable (context)
					as ITargetStructObject;
				if (sobj == null)
					throw new ScriptingException (
						"`{0}' is not a struct or class", left.Name);

				member = StructAccessExpression.FindMember (
					sobj.Type, sobj, name, true, true);
				if (member == null)
					throw new ScriptingException (
						"Type `{0}' has no member `{1}'",
						sobj.Type.Name, name);

				if (member.IsStatic)
					throw new ScriptingException (
						"Cannot access static member `{0}.{1}' with an " +
						"instance reference; use a type name instead.",
						sobj.Type.Name, name);

				return member;
			}

			Expression ltype = left.TryResolveType (context);
			if (ltype != null) {
				ITargetStructType stype = ltype.EvaluateType (context)
					as ITargetStructType;
				if (stype == null)
					throw new ScriptingException (
						"`{0}' is not a struct or class", ltype.Name);

				member = StructAccessExpression.FindMember (
					stype, null, name, true, true);
				if (member == null)
					throw new ScriptingException (
						"Type `{0}' has no member `{1}'",
						stype.Name, name);

				if (!member.IsStatic)
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
			return ResolveMemberAccess (context);
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

	public abstract class MemberExpression : Expression
	{
		public abstract ITargetStructObject InstanceObject {
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
		protected readonly ITargetStructType stype;
		protected readonly ITargetStructObject instance;
		protected readonly string name;
		protected readonly ArrayList methods;
		protected readonly bool is_instance, is_static;

		public MethodGroupExpression (ITargetStructType stype, ITargetStructObject instance,
					      string name, ArrayList methods, bool is_instance,
					      bool is_static)
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

		public override ITargetStructObject InstanceObject {
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
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

			TargetFunctionType func = OverloadResolve (context, types);
			if (func != null)
				return func;

			ArrayList list = new ArrayList ();
			foreach (ITargetMethodInfo method in methods) {
				if (method.Type.Source == null)
					continue;
				list.Add (method.Type.Source);
			}
			if (list.Count == 0)
				return null;
			SourceMethod[] sources = new SourceMethod [list.Count];
			list.CopyTo (sources, 0);
			throw new MultipleLocationsMatchException (sources);
		}

		public TargetFunctionType OverloadResolve (ScriptingContext context,
							    Expression[] types)
		{
			ArrayList candidates = new ArrayList ();

			foreach (ITargetMethodInfo method in methods) {
				if ((types != null) &&
				    (method.Type.ParameterTypes.Length != types.Length))
					continue;

				candidates.Add (method.Type);
			}

			if (candidates.Count == 1)
				return (TargetFunctionType) candidates [0];

			if (candidates.Count == 0)
				throw new ScriptingException (
					"No overload of method `{0}' has {1} arguments.",
					Name, types.Length);

			if (types == null)
				throw new ScriptingException (
					"Ambiguous method `{0}'; need to use " +
					"full name", Name);

			TargetFunctionType match = OverloadResolve (
				context, stype, types, candidates);

			if (match == null)
				throw new ScriptingException (
					"Ambiguous method `{0}'; need to use " +
					"full name", Name);

			return match;
		}

		public static TargetFunctionType OverloadResolve (ScriptingContext context,
								   ITargetStructType stype,
								   Expression[] types,
								   ArrayList candidates)
		{
			// We do a very simple overload resolution here
			ITargetType[] argtypes = new ITargetType [types.Length];
			for (int i = 0; i < types.Length; i++)
				argtypes [i] = types [i].EvaluateType (context);

			// Ok, no we need to find an exact match.
			TargetFunctionType match = null;
			foreach (TargetFunctionType method in candidates) {
				bool ok = true;
				for (int i = 0; i < types.Length; i++) {
					if (method.ParameterTypes [i] != argtypes [i]) {
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

	public class PropertyGroupExpression : Expression
	{
		ITargetStructType stype;
		ITargetStructObject instance;
		ILanguage language;
		string name;
		ArrayList props;

		public PropertyGroupExpression (ITargetStructType stype, string name,
						ITargetStructObject instance,
						ILanguage language, ArrayList props)
		{
			this.stype = stype;
			this.instance = instance;
			this.language = language;
			this.name = name;
			this.props = props;
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

		protected ITargetPropertyInfo OverloadResolve (ScriptingContext context,
							       bool getter,
							       ITargetType[] types)
		{
			ArrayList candidates = new ArrayList ();

			foreach (ITargetPropertyInfo prop in props) {
				if ((types != null) &&
				    (prop.Getter.ParameterTypes.Length != types.Length))
					continue;

				candidates.Add (prop);
			}

			if (candidates.Count == 1)
				return (ITargetPropertyInfo) candidates [0];

			if (candidates.Count == 0)
				throw new ScriptingException (
					"No overload of property `{0}' has {1} indices.",
					Name, types.Length);

			if (types == null)
				throw new ScriptingException (
					"Ambiguous property `{0}'; need to use " +
					"full name", Name);

			ITargetPropertyInfo match = OverloadResolve (
				context, language, stype, types, candidates);

			if (match == null)
				throw new ScriptingException (
					"Ambiguous property `{0}'; need to use " +
					"full name", Name);

			return match;
		}

		public static ITargetPropertyInfo OverloadResolve (ScriptingContext context,
								   ILanguage language,
								   ITargetStructType stype,
								   ITargetType[] types,
								   ArrayList candidates)
		{
			ITargetPropertyInfo match = null;
			foreach (ITargetPropertyInfo prop in candidates) {

				if (prop.Getter.ParameterTypes.Length != types.Length)
					continue;

				bool ok = true;
				for (int i = 0; i < types.Length; i++) {
					if (prop.Getter.ParameterTypes [i] != types [i]) {
						ok = false;
						break;
					}
				}

				if (!ok)
					continue;

				// We need to find exactly one match
				if (match != null)
					return null;

				match = prop;
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

	public class RegisterExpression : Expression
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

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			register = frame.FindRegister (name);
			return frame.GetRegisterType (register);
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			register = frame.FindRegister (name);
			try {
				return frame.GetRegister (register);
			} catch {
				throw new ScriptingException (
					"Can't access register `{0}' selected stack frame.");
			}
		}

		protected override bool DoAssign (ScriptingContext context, ITargetObject tobj)
		{
			TargetFundamentalObject fobj = tobj as TargetFundamentalObject;
			if (fobj == null)
				throw new ScriptingException (
					"Cannot store non-fundamental object `{0}' in " +
					" a registers", tobj);

			FrameHandle frame = context.CurrentFrame;
			object obj = fobj.GetObject (frame.Frame.TargetAccess);
			long value = Convert.ToInt64 (obj);
			register = frame.FindRegister (name);
			frame.SetRegister (register, value);
			return true;
		}
	}

	public class StructAccessExpression : MemberExpression
	{
		public readonly ITargetStructType Type;
		public readonly ITargetMemberInfo Member;
		protected readonly ITargetStructObject instance;

		protected StructAccessExpression (ITargetStructType type,
						  ITargetStructObject instance,
						  ITargetMemberInfo member)
		{
			this.Type = type;
			this.Member = member;
			this.instance = instance;
			resolved = true;
		}

		public override string Name {
			get { return Member.Name; }
		}

		public override ITargetStructObject InstanceObject {
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

		protected ITargetObject GetField (ITargetAccess target, ITargetFieldInfo field)
		{
			if (field.IsStatic)
				return Type.GetStaticField (target, field.Index);
			else
				return InstanceObject.GetField (field.Index);
		}

		protected ITargetObject GetProperty (ScriptingContext context,
						     ITargetPropertyInfo prop)
		{
			string exc_message;
			ITargetObject res = context.CurrentProcess.RuntimeInvoke (
				prop.Getter, InstanceObject, new ITargetObject [0],
				out exc_message);

			if (exc_message != null)
				throw new ScriptingException (
					"Invocation of `{0}' raised an exception: {1}",
					Name, exc_message);

			return res;
		}

		protected ITargetObject GetMember (ScriptingContext context, ITargetAccess target,
						   ITargetMemberInfo member)
		{
			if (member is ITargetPropertyInfo)
				return GetProperty (context, (ITargetPropertyInfo) member);
			else if (member is ITargetFieldInfo)
				return GetField (target, (ITargetFieldInfo) member);
			else
				throw new ScriptingException ("Member `{0}' is of unknown type {1}",
							      Name, member.GetType ());
		}

		public static MemberExpression FindMember (ITargetStructType stype,
							   ITargetStructObject instance, string name,
							   bool search_static, bool search_instance)
		{
		again:
			ITargetMemberInfo member = stype.FindMember (
				name, search_static, search_instance);

			if (member != null)
				return new StructAccessExpression (stype, instance, member);

			ArrayList methods = new ArrayList ();
			bool is_instance = false;
			bool is_static = false;

			if (name == ".ctor") {
				foreach (ITargetMethodInfo method in stype.Constructors) {
					methods.Add (method);
					is_instance = true;
				}
			} else if (name == ".cctor") {
				foreach (ITargetMethodInfo method in stype.StaticConstructors) {
					methods.Add (method);
					is_static = true;
				}
			} else {
				if (search_instance) {
					foreach (ITargetMethodInfo method in stype.Methods) {
						if (method.Name != name)
							continue;

						methods.Add (method);
						is_instance = true;
					}
				}

				if (search_static) {
					foreach (ITargetMethodInfo method in stype.StaticMethods) {
						if (method.Name != name)
							continue;

						methods.Add (method);
						is_static = true;
					}
				}
			}

			if (methods.Count > 0)
				return new MethodGroupExpression (
					stype, instance, name, methods, is_instance, is_static);

			ITargetClassType ctype = stype as ITargetClassType;
			if ((ctype != null) && ctype.HasParent) {
				stype = ctype.ParentType;
				instance = ((ITargetClassObject) instance).Parent;
				goto again;
			}

			return null;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			StackFrame frame = context.CurrentFrame.Frame;

			if (!Member.IsStatic && (InstanceObject == null))
				throw new ScriptingException (
					"Instance member `{0}' cannot be used in static context.", Name);

			try {
				return GetMember (context, frame.TargetAccess, Member);
			} catch (TargetException ex) {
				throw new ScriptingException ("Cannot access struct member `{0}': {1}",
							      Name, ex);
			}
		}

		public TargetFunctionType ResolveMethod (ScriptingContext context)
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
				ITargetPropertyInfo property = Member as ITargetPropertyInfo;
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
				ITargetEventInfo ev = Member as ITargetEventInfo;
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

		protected void SetField (ITargetAccess target, ITargetFieldInfo field, ITargetObject obj)
		{
			if (field.IsStatic)
				Type.SetStaticField (target, field.Index, obj);
			else
				InstanceObject.SetField (field.Index, obj);
		}

		protected override bool DoAssign (ScriptingContext context, ITargetObject obj)
		{
			if (Member is ITargetFieldInfo) {
				StackFrame frame = context.CurrentFrame.Frame;

				if (Member.Type != obj.Type)
					throw new ScriptingException (
						"Type mismatch: cannot assign expression of type " +
						"`{0}' to field `{1}', which is of type `{2}'.",
						obj.TypeName, Name, Member.Type.Name);

				SetField (frame.TargetAccess, (ITargetFieldInfo) Member, obj);
			}
			else if (Member is ITargetPropertyInfo) 
			  	throw new ScriptingException ("Can't set properties directly.");
			else if (Member is ITargetEventInfo)
				throw new ScriptingException ("Can't set events directly.");
			else if (Member is ITargetMethodInfo)
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

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			ITargetType type = expr.EvaluateType (context);

			ITargetPointerType ptype = type as ITargetPointerType;
			if (ptype != null)
				return ptype.StaticType;

			throw new ScriptingException (
				"Expression `{0}' is not a pointer.", expr.Name);
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			ITargetObject obj = expr.EvaluateVariable (context);

			ITargetPointerObject pobj = obj as ITargetPointerObject;
			if (pobj != null) {
				ITargetObject result;
				try {
					result = pobj.DereferencedObject;
				} catch {
					result = null;
				}

				if (result == null)
					throw new ScriptingException (
						"Cannot dereference `{0}'.", expr.Name);

				return result;
			}

			ITargetClassObject cobj = obj as ITargetClassObject;
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
				return new TargetAddress (context.GlobalAddressDomain, (long) obj);

			ITargetPointerObject pobj = obj as ITargetPointerObject;
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

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;

			ITargetPointerType ptype = expr.EvaluateType (context)
				as ITargetPointerType;
			if (ptype != null)
				return ptype;

			return frame.Language.PointerType;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;

			TargetAddress address = EvaluateAddress (context);

			return frame.Language.CreatePointer (frame.Frame, address);
		}

		public override TargetAddress EvaluateAddress (ScriptingContext context)
		{
			PointerExpression pexpr = expr as PointerExpression;
			if (pexpr != null)
				return pexpr.EvaluateAddress (context);

			ITargetPointerObject obj = expr.EvaluateVariable (context) as
				ITargetPointerObject;
			if (obj == null)
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

		int GetIntIndex (ITargetAccess target, Expression index, ScriptingContext context)
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

		int[] GetIntIndices (ITargetAccess target, ScriptingContext context)
		{
			int[] int_indices = new int [indices.Length];
			for (int i = 0; i < indices.Length; i++)
				int_indices [i] = GetIntIndex (target, indices [i], context);
			return int_indices;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			ITargetAccess target = context.CurrentFrame.Frame.TargetAccess;
			ITargetObject obj = expr.EvaluateVariable (context);

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
			ITargetPointerObject pobj = obj as ITargetPointerObject;
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
			ITargetStructObject sobj = obj as ITargetStructObject;
			if (sobj != null) {
				StackFrame frame = context.CurrentFrame.Frame;
				ITargetPropertyInfo prop_info;
				ArrayList candidates = new ArrayList ();

				candidates.AddRange (sobj.Type.Properties);

				ITargetType[] indextypes = new ITargetType [indices.Length];
				ITargetObject[] indexargs = new ITargetObject [indices.Length];
				for (int i = 0; i < indices.Length; i++) {
					indextypes [i] = indices [i].EvaluateType (context);
					if (indextypes [i] == null)
						return null;
					indexargs [i] = indices [i].EvaluateVariable (context);
					if (indexargs [i] == null)
						return null;
				}

				prop_info = PropertyGroupExpression.OverloadResolve (
					context, frame.Language, sobj.Type, indextypes, candidates);

				if (prop_info == null)
				 	throw new ScriptingException ("Could not find matching indexer.");

				if (!prop_info.CanRead)
					throw new ScriptingException (
						"Indexer `{0}' doesn't have a getter.", expr.Name);

				string exc_message;
				ITargetObject res = context.CurrentProcess.RuntimeInvoke (
					prop_info.Getter, sobj, indexargs, out exc_message);

				if (exc_message != null)
					throw new ScriptingException (
						"Invocation of `{0}' raised an exception: {1}",
						expr, exc_message);

				return res;
			}

			throw new ScriptingException (
				"{0} is neither an array/pointer type, nor is it " +
				"an object with a valid indexer.", expr);
		}

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			TargetArrayType type = expr.EvaluateType (context)
				as TargetArrayType;
			if (type == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", expr.Name);

			return type.ElementType;
		}

		protected override bool DoAssign (ScriptingContext context, ITargetObject right)
		{
			ITargetAccess target = context.CurrentFrame.Frame.TargetAccess;
			ITargetObject obj = expr.EvaluateVariable (context);

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
			ITargetStructObject sobj = obj as ITargetStructObject;
			if (sobj != null) {
				StackFrame frame = context.CurrentFrame.Frame;
				ITargetPropertyInfo prop_info;
				ArrayList candidates = new ArrayList ();

				candidates.AddRange (sobj.Type.Properties);

				ITargetType[] indextypes = new ITargetType [indices.Length];
				ITargetObject[] indexargs = new ITargetObject [indices.Length + 1];
				for (int i = 0; i < indices.Length; i++) {
					indextypes [i] = indices [i].EvaluateType (context);
					if (indextypes [i] == null)
						return false;
					indexargs [i] = indices [i].EvaluateVariable (context);
					if (indexargs [i] == null)
						return false;
				}

				indexargs [indices.Length] = right;

				prop_info = PropertyGroupExpression.OverloadResolve (
					context, frame.Language, sobj.Type, indextypes, candidates);

				if (prop_info == null)
				 	throw new ScriptingException ("Could not find matching indexer.");

				if (!prop_info.CanWrite)
					throw new ScriptingException (
						"Indexer `{0}' doesn't have a setter.", expr.Name);

				string exc_message;
				context.CurrentProcess.RuntimeInvoke (
					prop_info.Setter, sobj, indexargs, out exc_message);

				if (exc_message != null)
					throw new ScriptingException (
						"Invocation of `{0}' raised an exception: {1}",
						expr, exc_message);

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

		static ITargetClassObject TryParentCast (ScriptingContext context,
							 ITargetClassObject source,
							 ITargetClassType source_type,
							 ITargetClassType target_type)
		{
			if (source_type == target_type)
				return source;

			if (!source_type.HasParent)
				return null;

			source = TryParentCast (
				context, source, source_type.ParentType, target_type);
			if (source == null)
				return null;

			return source.Parent;
		}

		static ITargetClassObject TryCurrentCast (ScriptingContext context,
							  ITargetClassObject source,
							  ITargetClassType source_type,
							  ITargetClassType target_type)
		{
			ITargetClassObject current = source;
			if (current.Type == source_type)
				return null;

			return TryParentCast (context, current, current.Type, target_type);
		}

		public static ITargetObject TryCast (ScriptingContext context, ITargetObject source,
						     ITargetClassType target_type)
		{
			if (source.Type == target_type)
				return source;

			ITargetClassObject sobj = source as ITargetClassObject;
			if (sobj == null)
				return null;

			ITargetClassObject result;
			result = TryParentCast (context, sobj, sobj.Type, target_type);
			if (result != null)
				return result;

			return TryCurrentCast (context, sobj, sobj.Type, target_type);
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			ITargetClassType type = target.EvaluateType (context)
				as ITargetClassType;
			if (type == null)
				throw new ScriptingException (
					"Type {0} is not a class type.", target.Name);

			ITargetClassObject source = expr.EvaluateVariable (context)
				as ITargetClassObject;
			if (source == null)
				throw new ScriptingException (
					"Variable {0} is not a class type.", expr.Name);

			ITargetObject obj = TryCast (context, source, type);
			if (obj == null)
				throw new ScriptingException (
					"Cannot cast from {0} to {1}.", source.Type.Name,
					type.Name);

			return obj;
		}

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			ITargetObject obj = EvaluateVariable (context);
			if (obj == null)
				return null;

			return obj.Type;
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
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

			return cond ? true_expr.EvaluateVariable (context) : false_expr.EvaluateVariable (context);
		}
	}

	public class InvocationExpression : Expression
	{
		Expression method_expr;
		Expression[] arguments;
		MethodGroupExpression mg;
		string name;

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
			ITargetStructObject sobj = expr.EvaluateVariable (context)
				as ITargetStructObject;
			if (sobj == null)
				return null;

			ITargetClassType delegate_type = sobj.Type.Language.DelegateType;
			if (CastExpression.TryCast (context, sobj, delegate_type) == null)
				return null;

			ITargetMemberInfo invoke = null;
			foreach (ITargetMemberInfo member in sobj.Type.Methods) {
				if (member.Name == "Invoke") {
					invoke = member;
					break;
				}
			}

			if (invoke == null)
				return null;

			ArrayList list = new ArrayList ();
			list.Add (invoke);

			MethodGroupExpression mg = new MethodGroupExpression (
				sobj.Type, sobj, "Invoke", list, true, false);
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

			resolved = true;
			return this;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return Invoke (context, false);
		}

		protected override TargetFunctionType DoEvaluateMethod (ScriptingContext context,
									 LocationType type,
									 Expression[] types)
		{
			return method_expr.EvaluateMethod (context, type, types);
		}

		public ITargetObject Invoke (ScriptingContext context, bool debug)
		{
			Expression[] args = new Expression [arguments.Length];
			for (int i = 0; i < arguments.Length; i++) {
				args [i] = arguments [i].Resolve (context);
				if (args [i] == null)
					return null;
			}

			TargetFunctionType func = mg.OverloadResolve (context, args);

			ITargetObject[] objs = new ITargetObject [args.Length];
			for (int i = 0; i < args.Length; i++)
				objs [i] = args [i].EvaluateVariable (context);

			ITargetObject instance = mg.InstanceObject;

			try {
				if (debug) {
					context.CurrentProcess.RuntimeInvoke (func, instance, objs);
					return null;
				}

				string exc_message;
				ITargetObject retval = context.CurrentProcess.RuntimeInvoke (
					func, mg.InstanceObject, objs, out exc_message);

				if (exc_message != null)
					throw new ScriptingException (
						"Invocation of `{0}' raised an exception: {1}",
						Name, exc_message);

				if (!func.HasReturnValue)
					throw new ScriptingException (
						"Method `{0}' doesn't return a value.", Name);

				return retval;
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Invocation of `{0}' raised an exception: {1}", Name, ex.Message);
			}
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

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			return type_expr.EvaluateType (context);
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return Invoke (context, false);
		}

		public ITargetObject Invoke (ScriptingContext context, bool debug)
		{
			ITargetStructType stype = type_expr.EvaluateType (context)
				as ITargetStructType;
			if (stype == null)
				throw new ScriptingException (
					"Type `{0}' is not a struct or class.",
					type_expr.Name);

			ArrayList candidates = new ArrayList ();
			candidates.AddRange (stype.Constructors);

			TargetFunctionType ctor;
			if (candidates.Count == 0)
				throw new ScriptingException (
					"Type `{0}' has no public constructor.",
					type_expr.Name);
			else if (candidates.Count == 1)
				ctor = ((ITargetMethodInfo) candidates [0]).Type;
			else
				ctor = MethodGroupExpression.OverloadResolve (
					context, stype, arguments, candidates);

			if (ctor == null)
				throw new ScriptingException (
					"Type `{0}' has no constructor which is applicable " +
					"for your list of arguments.", type_expr.Name);

			ITargetObject[] args = new ITargetObject [arguments.Length];
			for (int i = 0; i < arguments.Length; i++)
				args [i] = arguments [i].EvaluateVariable (context);

			string exc_message;
			ITargetObject res = context.CurrentProcess.RuntimeInvoke (
				ctor, null, args, out exc_message);

			if (exc_message != null)
				throw new ScriptingException (
					"Invocation of type `{0}'s constructor raised an " +
					"exception: {1}", type_expr.Name, exc_message);

			return res;
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			ITargetObject obj;
			if (right is NullExpression) {
				StackFrame frame = context.CurrentFrame.Frame;
				ITargetType ltype = left.EvaluateType (context);
				obj = frame.Language.CreateNullObject (frame.TargetAccess, ltype);
			} else
				obj = right.EvaluateVariable (context);

			left.Assign (context, obj);
			return obj;
		}
	}
}
