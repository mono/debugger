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
			try {
				ITargetMethodInfo method = OverloadResolve (context, types);
				return new SourceLocation (method.Type.Source);
			} catch {
				ArrayList list = new ArrayList ();
				foreach (ITargetMethodInfo method in methods) {
					if (method.Type.Source == null)
						continue;
					list.Add (method.Type.Source);
				}
				SourceMethod[] sources = new SourceMethod [list.Count];
				list.CopyTo (sources, 0);
				context.AddMethodSearchResult (sources);
				throw new ScriptingException ("");
			}
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
		public abstract TargetLocation EvaluateAddress (ScriptingContext context);
	}

	public class RegisterExpression : PointerExpression
	{
		string name;
		int register;
		long offset;

		public RegisterExpression (string register, long offset)
		{
			this.name = register;
			this.offset = offset;
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
			return context.CurrentFrame.GetRegister (register, offset);
		}

		public override TargetLocation EvaluateAddress (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			register = frame.FindRegister (name);
			return frame.GetRegisterLocation (register, offset, true);
		}

		protected override bool DoAssign (ScriptingContext context, ITargetObject tobj)
		{
			if (offset != 0)
				throw new ScriptingException (
					"Cannot assign a register expression which " +
					"has an offset.");

			ITargetFundamentalObject fobj = tobj as ITargetFundamentalObject;
			if ((fobj == null) || !fobj.HasObject)
				throw new ScriptingException (
					"Cannot store non-fundamental object `{0}' in " +
					" a registers", tobj);

			object obj = fobj.Object;
			long value = Convert.ToInt64 (obj);
			context.CurrentFrame.SetRegister (register, value);
			return true;
		}
	}

	public class StructAccessExpression : Expression
	{
		string name;

		public readonly string Identifier;
		public readonly bool IsStatic;

		new ITargetStructType Type;
		ITargetStructObject Instance;
		StackFrame Frame;

		protected StructAccessExpression (StackFrame frame, ITargetStructType type,
						  string identifier)
		{
			this.Frame = frame;
			this.Type = type;
			this.Identifier = identifier;
			this.IsStatic = true;
			resolved = true;
		}

		protected StructAccessExpression (StackFrame frame,
						  ITargetStructObject instance,
						  string identifier)
		{
			this.Frame = frame;
			this.Type = instance.Type;
			this.Instance = instance;
			this.Identifier = identifier;
			this.IsStatic = false;
			resolved = true;
		}

		public override string Name {
			get {
				return Identifier;
			}
		}

		protected override Expression DoResolve (ScriptingContext context)
		{
			return this;
		}

		protected ITargetObject GetField (ITargetStructObject sobj, ITargetFieldInfo field)
		{
			try {
				return sobj.GetField (field.Index);
			} catch (TargetInvocationException ex) {
				throw new ScriptingException ("Can't get field {0}: {1}", Name, ex.Message);
			}
		}

		protected ITargetObject GetStaticField (ITargetStructType stype, StackFrame frame, ITargetFieldInfo field)
		{
			try {
				return stype.GetStaticField (frame, field.Index);
			} catch (TargetInvocationException ex) {
				throw new ScriptingException ("Can't get field {0}: {1}", Name, ex.Message);
			}
		}

		protected ITargetObject GetProperty (ITargetStructObject sobj, ITargetPropertyInfo property)
		{
			try {
				return sobj.GetProperty (property.Index);
			} catch (TargetInvocationException ex) {
				throw new ScriptingException ("Can't get property {0}: {1}", Name, ex.Message);
			}
		}

		protected ITargetObject GetStaticProperty (ITargetStructType stype, StackFrame frame, ITargetPropertyInfo property)
		{
			try {
				return stype.GetStaticProperty (frame, property.Index);
			} catch (TargetInvocationException ex) {
				throw new ScriptingException ("Can't get property {0}: {1}", Name, ex.Message);
			}
		}

		protected ITargetObject GetMember (ITargetStructObject sobj, ITargetMemberInfo member)
		{
			if (member is ITargetPropertyInfo)
				return GetProperty (sobj, (ITargetPropertyInfo) member);
			else
				return GetField (sobj, (ITargetFieldInfo) member);
		}

		protected ITargetObject GetStaticMember (ITargetStructType stype, StackFrame frame, ITargetMemberInfo member)
		{
			if (member is ITargetPropertyInfo)
				return GetStaticProperty (stype, frame, (ITargetPropertyInfo) member);
			else
				return GetStaticField (stype, frame, (ITargetFieldInfo) member);
		}

		public static ITargetMemberInfo FindMember (ITargetStructType stype,
							    bool is_static, string name)
		{
			if (!is_static) {
				foreach (ITargetFieldInfo field in stype.Fields)
					if (field.Name == name)
						return field;

				foreach (ITargetPropertyInfo property in stype.Properties)
					if (property.Name == name)
						return property;
			}

			foreach (ITargetFieldInfo field in stype.StaticFields)
				if (field.Name == name)
					return field;

			foreach (ITargetPropertyInfo property in stype.StaticProperties)
				if (property.Name == name)
					return property;

			return null;
		}

		public static Expression FindMember (ITargetStructType stype, StackFrame frame,
						     ITargetStructObject instance, string name)
		{
			ITargetMemberInfo member = FindMember (stype, instance != null, name);
			if (member != null) {
				if (instance != null)
					return new StructAccessExpression (frame, instance, name);
				else
					return new StructAccessExpression (frame, stype, name);
			}

			ArrayList methods = new ArrayList ();

		again:
			if (instance != null) {
				foreach (ITargetMethodInfo method in stype.Methods) {
					if (method.Name != name)
						continue;

					methods.Add (method);
				}
			}

			foreach (ITargetMethodInfo method in stype.StaticMethods) {
				if (method.Name != name)
					continue;

				methods.Add (method);
			}

			if (methods.Count > 0)
				return new MethodGroupExpression (
					stype, name, instance, frame.Language, methods);

			ITargetClassType ctype = stype as ITargetClassType;
			if ((ctype != null) && ctype.HasParent) {
				stype = ctype.ParentType;
				goto again;
			}

			return null;
		}

		protected ITargetMemberInfo FindMember (ScriptingContext context, bool report_error)
		{
			ITargetMemberInfo member = FindMember (Type, IsStatic, Identifier);
			if ((member != null) || !report_error)
				return member;

			if (IsStatic)
				throw new ScriptingException ("Type {0} has no static member {1}.", Type.Name, Identifier);
			else
				throw new ScriptingException ("Type {0} has no member {1}.", Type.Name, Identifier);
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			ITargetMemberInfo member = FindMember (context, true);

			if (member.IsStatic)
				return GetStaticMember (Type, Frame, member);
			else if (!IsStatic)
				return GetMember (Instance, member);
			else
				throw new ScriptingException ("Instance member {0} cannot be used in static context.", Name);
		}
	}

	public class PointerDereferenceExpression : PointerExpression
	{
		Expression expr;
		string name;

		public PointerDereferenceExpression (Expression expr)
		{
			this.expr = expr;
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

			return this;
		}

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;

			ITargetPointerType ptype = expr.EvaluateType (context)
				as ITargetPointerType;
			if (ptype == null)
				throw new ScriptingException (
					"Expression `{0}' is not a pointer.", expr.Name);

			return ptype;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			ITargetPointerObject pobj = expr.EvaluateVariable (context)
				as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException (
					"Expression `{0}' is not a pointer type.", expr.Name);

			if (!pobj.HasDereferencedObject)
				throw new ScriptingException (
					"Cannot dereference `{0}'.", expr.Name);

			return pobj.DereferencedObject;
		}

		public override TargetLocation EvaluateAddress (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;

			object obj = expr.Resolve (context);
			if (obj is int)
				obj = (long) (int) obj;
			if (obj is long) {
				ITargetType type = frame.Frame.Language.PointerType;

				TargetAddress taddress = new TargetAddress (
					frame.Frame.AddressDomain, (long) obj);

				return new AbsoluteTargetLocation (frame.Frame, taddress);
			}

			ITargetPointerObject pobj = obj as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException (
					"Expression `{0}' is not a pointer type.", expr.Name);

			return pobj.Location;
		}
	}

	public class ArrayAccessExpression : Expression
	{
		Expression expr, index;
		string name;

		public ArrayAccessExpression (Expression expr, Expression index)
		{
			this.expr = expr;
			this.index = index;

			name = String.Format ("{0}[{1}]", expr.Name, index);
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

			index = index.Resolve (context);
			if (index == null)
				return null;

			resolved = true;
			return this;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			int i;

			ITargetArrayObject obj = expr.EvaluateVariable (context)
				as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", expr.Name);
			try {
				i = (int) this.index.Evaluate (context);
			} catch (Exception e) {
				throw new ScriptingException (
					"Cannot convert {0} to an integer for indexing: {1}",
					this.index, e);
			}

			if ((i < obj.LowerBound) || (i >= obj.UpperBound))
				throw new ScriptingException (
					"Index {0} of array expression {1} out of bounds " +
					"(must be between {2} and {3}).", i, expr.Name,
					obj.LowerBound, obj.UpperBound - 1);

			return obj [i];
		}

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			ITargetArrayType type = expr.EvaluateType (context)
				as ITargetArrayType;
			if (type == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", expr.Name);

			return type.ElementType;
		}
	}

	public class ParentClassExpression : Expression
	{
		Expression expr;
		string name;

		public ParentClassExpression (Expression expr)
		{
			this.expr = expr;
			this.name = String.Format ("parent ({0})", expr.Name);
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

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			ITargetClassObject obj = expr.EvaluateVariable (context)
				as ITargetClassObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not a class type.", expr.Name);

			if (!obj.Type.HasParent)
				throw new ScriptingException (
					"Variable {0} doesn't have a parent type.",
					expr.Name);

			return obj.Parent;
		}

		protected override ITargetType DoEvaluateType (ScriptingContext context)
		{
			ITargetClassType type = expr.ResolveType (context) as ITargetClassType;
			if (type == null)
				throw new ScriptingException (
					"Variable {0} is not a class type.", expr.Name);

			if (!type.HasParent)
				throw new ScriptingException (
					"Variable {0} doesn't have a parent type.", expr.Name);

			return type.ParentType;
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

		protected override Expression DoResolve (ScriptingContext context)
		{
			method_expr = method_expr.Resolve (context);
			if (method_expr == null)
				return null;

			mg = method_expr as MethodGroupExpression;
			if (mg == null)
				throw new ScriptingException (
					"Expression `{0}' is not a method.", method_expr.Name);

			resolved = true;
			return this;
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return Invoke (context, false);
		}

		protected override SourceLocation DoEvaluateLocation (ScriptingContext context,
								      Expression[] types)
		{
			Expression[] argtypes = new Expression [arguments.Length];
			for (int i = 0; i < arguments.Length; i++) {
				argtypes [i] = arguments [i].ResolveType (context);
				if (argtypes [i] == null)
					return null;
			}

			return method_expr.EvaluateLocation (context, argtypes);
		}

		public ITargetObject Invoke (ScriptingContext context, bool debug)
		{
			Expression[] args = new Expression [arguments.Length];
			for (int i = 0; i < arguments.Length; i++) {
				args [i] = arguments [i].Resolve (context);
				if (args [i] == null)
					return null;
			}

			ITargetFunctionObject func = mg.EvaluateMethod (
				context, context.CurrentFrame.Frame, args);

			ITargetObject[] objs = new ITargetObject [args.Length];
			for (int i = 0; i < args.Length; i++)
				objs [i] = args [i].EvaluateVariable (context);

			try {
				ITargetObject retval = func.Invoke (objs, debug);
				if (!debug && !func.Type.HasReturnValue)
					throw new ScriptingException ("Method `{0}' doesn't return a value.", Name);

				return retval;
			} catch (MethodOverloadException ex) {
				throw new ScriptingException ("Cannot invoke method `{0}': {1}", Name, ex.Message);
			} catch (TargetInvocationException ex) {
				throw new ScriptingException ("Invocation of `{0}' raised an exception: {1}", Name, ex.Message);
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
			FrameHandle frame = context.CurrentFrame;

			ITargetStructType stype = type_expr.EvaluateType (context)
				as ITargetStructType;
			if (stype == null)
				throw new ScriptingException (
					"Type `{0}' is not a struct or class.",
					type_expr.Name);

			ArrayList candidates = new ArrayList ();
			candidates.AddRange (stype.Constructors);

			ITargetMethodInfo method;
			if (candidates.Count == 0)
				throw new ScriptingException (
					"Type `{0}' has no public constructor.",
					type_expr.Name);
			else if (candidates.Count == 1)
				method = (ITargetMethodInfo) candidates [0];
			else
				method = MethodGroupExpression.OverloadResolve (
					context, frame.Frame.Language, stype, arguments,
					candidates);

			if (method == null)
				throw new ScriptingException (
					"Type `{0}' has no constructor which is applicable " +
					"for your list of arguments.", type_expr.Name);

			ITargetFunctionObject ctor = stype.GetConstructor (
				frame.Frame, method.Index);

			ITargetObject[] args = new ITargetObject [arguments.Length];
			for (int i = 0; i < arguments.Length; i++)
				args [i] = arguments [i].EvaluateVariable (context);

			try {
				return ctor.Type.InvokeStatic (frame.Frame, args, debug);
			} catch (MethodOverloadException ex) {
				throw new ScriptingException (
					"Cannot invoke constructor on type `{0}': {1}",
					type_expr.Name, ex.Message);
			} catch (TargetInvocationException ex) {
				throw new ScriptingException (
					"Invocation of type `{0}'s constructor raised an " +
					"exception: {1}", type_expr.Name, ex.Message);
			}
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
			ITargetObject obj = right.EvaluateVariable (context);
			left.Assign (context, obj);
			return obj;
		}
	}
}
