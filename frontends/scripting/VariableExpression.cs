using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	[Expression("type_expression", "Type expression")]
	public abstract class TypeExpression : Expression
	{
		public abstract string Name {
			get;
		}

		protected abstract ITargetType DoResolveType (ScriptingContext context);

		public ITargetType ResolveType (ScriptingContext context)
		{
			ITargetType type = DoResolveType (context);
			if (type == null)
				throw new ScriptingException ("Can't get type `{0}'.", Name);

			return type;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return ResolveType (context);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), Name);
		}
	}

	public class TypeOfExpression : TypeExpression
	{
		VariableExpression var_expr;

		public TypeOfExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		public override string Name {
			get { return String.Format ("typeof ({0})", var_expr.Name); }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			return var_expr.ResolveType (context);
		}
	}

	public class TypeNameExpression : TypeExpression
	{
		FrameExpression frame_expr;
		string identifier;

		public TypeNameExpression (FrameExpression frame_expr, string identifier)
		{
			this.frame_expr = frame_expr;
			this.identifier = identifier;
		}

		public override string Name {
			get { return identifier; }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			return frame.Frame.Language.LookupType (frame.Frame, identifier);
		}
	}

	[Expression("variable_expression", "Variable expression")]
	public abstract class VariableExpression : TypeExpression
	{
		protected abstract ITargetObject DoResolveVariable (ScriptingContext context);

		public ITargetObject ResolveVariable (ScriptingContext context)
		{
			try {
				ITargetObject retval = DoResolveVariable (context);
				if (retval == null)
					throw new ScriptingException ("Can't resolve variable: `{0}'", Name);

				return retval;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException ("Location of variable {0} is invalid: {1}",
							      Name, ex.Message);
			}
		}

		protected virtual ITargetFunctionObject DoResolveMethod (ScriptingContext context, Expression[] args)
		{
			throw new ScriptingException ("Variable is not a method: `{0}'", Name);
		}

		public ITargetFunctionObject ResolveMethod (ScriptingContext context, Expression[] args)
		{
			try {
				ITargetFunctionObject retval = DoResolveMethod (context, args);
				if (retval == null)
					throw new ScriptingException ("Can't resolve variable: `{0}'", Name);

				return retval;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException ("Location of variable {0} is invalid: {1}",
							      Name, ex.Message);
			}
		}

		protected override object DoResolve (ScriptingContext context)
		{
			try {
				ITargetObject obj = ResolveVariable (context);
				if (!obj.IsValid)
					throw new ScriptingException ("Variable `{0}' is out of scope.", Name);

				return obj;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException ("Location of variable `{0}' is invalid: {1}",
							      Name, ex);
			}
		}

		public virtual void Assign (ScriptingContext context, object obj)
		{
			ITargetObject target_object = ResolveVariable (context);

			ITargetFundamentalObject fundamental = target_object as ITargetFundamentalObject;
			if ((fundamental == null) || !fundamental.HasObject)
				throw new ScriptingException ("Modifying variables of this type is not yet supported.");

			try {
				fundamental.Object = obj;
			} catch (NotSupportedException) {
				throw new ScriptingException ("Modifying variables of this type is not yet supported.");
			} catch (InvalidOperationException) {
				throw new ScriptingException ("Modifying variables of this type is not yet supported.");
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), Name);
		}
	}

	public class ScriptingVariableReference : VariableExpression
	{
		string identifier;

		public ScriptingVariableReference (string identifier)
		{
			this.identifier = identifier;
		}

		public override string Name {
			get { return '!' + identifier; }
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			VariableExpression expr = context [identifier];
			if (expr == null)
				return null;

			return expr.ResolveVariable (context);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			VariableExpression expr = context [identifier];
			if (expr == null)
				return null;

			return expr.ResolveType (context);
		}
	}

	public class LastObjectExpression : VariableExpression
	{
		public override string Name {
			get { return "!!"; }
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			return context.LastObject;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			if (context.LastObject == null)
				return null;

			return context.LastObject.Type;
		}
	}

	public class VariableReferenceExpression : VariableExpression
	{
		FrameExpression frame_expr;
		string identifier;

		public VariableReferenceExpression (FrameExpression frame_expr, string identifier)
		{
			this.frame_expr = frame_expr;
			this.identifier = identifier;
		}

		public override string Name {
			get { return '$' + identifier; }
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			return frame.GetVariable (identifier);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			FrameHandle frame = (FrameHandle) frame_expr.Resolve (context);

			return frame.GetVariableType (identifier);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2})", GetType (), frame_expr, identifier);
		}
	}

	public class RegisterExpression : VariableExpression
	{
		FrameExpression frame_expr;
		string register;
		long offset;
		FrameHandle frame;

		public RegisterExpression (FrameExpression frame_expr, string register, long offset)
		{
			this.frame_expr = frame_expr;
			this.register = register;
			this.offset = offset;
		}

		public override string Name {
			get { return '%' + register; }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			frame = (FrameHandle) frame_expr.Resolve (context);

			return frame.GetRegisterType (register);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetType type = DoResolveType (context);

			return frame.GetRegister (register, offset);
		}

		public override void Assign (ScriptingContext context, object obj)
		{
			if (offset != 0)
				throw new ScriptingException ("Cannot assign a register expression which has an offset.");

			frame = (FrameHandle) frame_expr.Resolve (context);
			long value = Convert.ToInt64 (obj);

			frame.SetRegister (register, value);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2})", GetType (), frame_expr, register);
		}
	}

	public class StructAccessExpression : VariableExpression
	{
		FrameExpression frame_expr;
		VariableExpression instance_expr;
		TypeExpression type_expr;
		string name;

		public readonly string Identifier;
		public readonly bool IsStatic;

		ITargetStructType Type;
		ITargetStructObject Instance;
		StackFrame Frame;

		public StructAccessExpression (FrameExpression frame_expr, TypeExpression type_expr, string identifier)
		{
			this.frame_expr = frame_expr;
			this.type_expr = type_expr;
			this.Identifier = identifier;
			this.name = String.Concat (type_expr.Name, ".", identifier);
			this.IsStatic = true;
		}

		public StructAccessExpression (VariableExpression instance_expr, string identifier)
		{
			this.type_expr = instance_expr;
			this.instance_expr = instance_expr;
			this.Identifier = identifier;
			this.name = String.Concat (instance_expr.Name, ".", identifier);
			this.IsStatic = false;
		}

		public override string Name {
			get {
				return name;
			}
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

		protected ITargetMemberInfo FindMember (ITargetStructType stype)
		{
			if (!IsStatic) {
				foreach (ITargetFieldInfo field in stype.Fields)
					if (field.Name == Identifier)
						return field;

				foreach (ITargetPropertyInfo property in stype.Properties)
					if (property.Name == Identifier)
						return property;
			}

			foreach (ITargetFieldInfo field in stype.StaticFields)
				if (field.Name == Identifier)
					return field;

			foreach (ITargetPropertyInfo property in stype.StaticProperties)
				if (property.Name == Identifier)
					return property;

			return null;
		}

		protected ITargetMethodInfo OverloadResolve (ScriptingContext context, ITargetStructType stype,
							     Expression[] args, ArrayList candidates)
		{
			// We do a very simple overload resolution here
			VariableExpression[] vargs = new VariableExpression [args.Length];
			ITargetType[] argtypes = new ITargetType [args.Length];
			for (int i = 0; i < args.Length; i++) {
				// First of all, all arguments must be VariableExpressions
				vargs [i] = args [i] as VariableExpression;
				if (vargs [i] == null)
					return null;

				argtypes [i] = vargs [i].ResolveType (context);
			}

			// Ok, no we need to find an exact match.
			ITargetMethodInfo match = null;
			foreach (ITargetMethodInfo method in candidates) {
				bool ok = true;
				for (int i = 0; i < args.Length; i++) {
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

		protected ITargetMethodInfo FindMethod (ScriptingContext context, ITargetStructType stype, Expression[] args)
		{
		again:
			bool found_match = false;
			ArrayList candidates = new ArrayList ();

			if (!IsStatic) {
				foreach (ITargetMethodInfo method in stype.Methods) {
					if (method.Name != Identifier)
						continue;

					if ((args != null) && (method.Type.ParameterTypes.Length != args.Length)) {
						found_match = true;
						continue;
					}

					candidates.Add (method);
				}
			}

			foreach (ITargetMethodInfo method in stype.StaticMethods) {
				if (method.Name != Identifier)
					continue;

				if ((args != null) && (method.Type.ParameterTypes.Length != args.Length)) {
					found_match = true;
					continue;
				}

				candidates.Add (method);
			}

			if (candidates.Count == 1)
				return (ITargetMethodInfo) candidates [0];

			if (candidates.Count > 1) {
				ITargetMethodInfo retval = null;
				if (args != null)
					retval = OverloadResolve (context, stype, args, candidates);
				if (retval == null)
					throw new ScriptingException ("Ambiguous method `{0}'; need to use full name", Name);
				return retval;
			}

			ITargetClassType ctype = stype as ITargetClassType;
			if ((ctype != null) && ctype.HasParent) {
				stype = ctype.ParentType;
				goto again;
			}

			if (found_match && (args != null))
				throw new ScriptingException ("No overload of method `{0}' has {1} arguments.",
							      Name, args.Length);

			return null;
		}

		protected ITargetMemberInfo ResolveTypeBase (ScriptingContext context, bool report_error)
		{
			Type = type_expr.ResolveType (context) as ITargetStructType;
			if (Type == null)
				throw new ScriptingException (
					"Type `{0}' is not a struct or class type.", type_expr.Name);

			ITargetMemberInfo member = FindMember (Type);
			if ((member != null) || !report_error)
				return member;

			if (IsStatic)
				throw new ScriptingException ("Type {0} has no static member {1}.", Type.Name, Identifier);
			else
				throw new ScriptingException ("Type {0} has no member {1}.", Type.Name, Identifier);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			return ResolveTypeBase (context, true).Type;
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetMemberInfo member = ResolveTypeBase (context, true);

			if (!IsStatic) {
				Instance = instance_expr.ResolveVariable (context) as ITargetStructObject;
				if (Instance == null)
					throw new ScriptingException ("Variable {0} is not a struct or class type.",
								      instance_expr.Name);

				Frame = Instance.Location.StackFrame;
			} else {
				Frame = ((FrameHandle) frame_expr.Resolve (context)).Frame;
			}

			if (member.IsStatic)
				return GetStaticMember (Type, Frame, member);
			else if (!IsStatic)
				return GetMember (Instance, member);
			else
				throw new ScriptingException ("Instance member {0} cannot be used in static context.", Name);
		}

		protected override ITargetFunctionObject DoResolveMethod (ScriptingContext context, Expression[] args)
		{
			ITargetMemberInfo member = ResolveTypeBase (context, false);
			if (member != null)
				throw new ScriptingException ("Member {0} of type {1} is not a method.", Identifier, Type.Name);

			if (!IsStatic) {
				Instance = instance_expr.ResolveVariable (context) as ITargetStructObject;
				if (Instance == null)
					throw new ScriptingException ("Variable {0} is not a struct or class type.",
								      instance_expr.Name);

				Frame = Instance.Location.StackFrame;
			} else {
				Frame = ((FrameHandle) frame_expr.Resolve (context)).Frame;
			}

			ITargetMethodInfo method;
			if (Identifier.IndexOf ('(') != -1)
				method = FindMethod (context, Type, null);
			else
				method = FindMethod (context, Type, args);

			if (method != null) {
				if (method.IsStatic)
					return Type.GetStaticMethod (Frame, method.Index);
				else if (!IsStatic)
					return Instance.GetMethod (method.Index);
				else
					throw new ScriptingException ("Instance method {0} cannot be used in static context.", Name);
			}

			if (IsStatic)
				throw new ScriptingException ("Type {0} has no static method {1}.", Type.Name, Identifier);
			else
				throw new ScriptingException ("Type {0} has no method {1}.", Type.Name, Identifier);
		}
	}

	public class VariableDereferenceExpression : VariableExpression
	{
		VariableExpression var_expr;

		public VariableDereferenceExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		public override string Name {
			get {
				return '*' + var_expr.Name;
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetPointerObject pobj = var_expr.ResolveVariable (context) as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException ("Variable {0} is not a pointer type.",
							      var_expr.Name);

			if (!pobj.HasDereferencedObject)
				throw new ScriptingException ("Cannot dereference {0}.", var_expr.Name);

			return pobj.DereferencedObject;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetPointerObject pobj = var_expr.ResolveVariable (context) as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException ("Variable {0} is not a pointer type.",
							      var_expr.Name);

			ITargetType type = pobj.CurrentType;
			if (type == null)
				throw new ScriptingException ("Cannot get current type of {0}.", var_expr.Name);

			return type;
		}
	}

	public class ArrayAccessExpression : VariableExpression
	{
		VariableExpression var_expr;
		Expression index;

		public ArrayAccessExpression (VariableExpression var_expr, Expression index)
		{
			this.var_expr = var_expr;
			this.index = index;
		}

		public override string Name {
			get {
				return String.Format ("{0}[{1}]", var_expr.Name, index);
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			int i;

			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);
			try {
				i = (int) this.index.Resolve (context);
			} catch (Exception e) {
				throw new ScriptingException (
					"Cannot convert {0} to an integer for indexing: {1}", this.index, e);
			}

			if ((i < obj.LowerBound) || (i >= obj.UpperBound))
				throw new ScriptingException (
					"Index {0} of array expression {1} out of bounds " +
					"(must be between {2} and {3}).", i, var_expr.Name,
					obj.LowerBound, obj.UpperBound - 1);

			return obj [i];
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetArrayType type = var_expr.ResolveType (context) as ITargetArrayType;
			if (type == null)
				throw new ScriptingException ("Variable {0} is not an array type.",
							      var_expr.Name);

			return type.ElementType;
		}
	}

	public class ParentClassExpression : VariableExpression
	{
		VariableExpression var_expr;

		public ParentClassExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		public override string Name {
			get {
				return String.Format ("parent ({0})", var_expr.Name);
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetClassObject obj = var_expr.ResolveVariable (context) as ITargetClassObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not a class type.", var_expr.Name);

			if (!obj.Type.HasParent)
				throw new ScriptingException ("Variable {0} doesn't have a parent type.",
							      var_expr.Name);

			return obj.Parent;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetClassType type = var_expr.ResolveType (context) as ITargetClassType;
			if (type == null)
				throw new ScriptingException ("Variable {0} is not a class type.",
							      var_expr.Name);

			if (!type.HasParent)
				throw new ScriptingException ("Variable {0} doesn't have a parent type.",
							      var_expr.Name);

			return type.ParentType;
		}
	}

	public class PointerExpression : VariableExpression
	{
		FrameExpression frame_expr;
		FrameHandle frame;
		TargetLocation location;
		long address;

		public PointerExpression (FrameExpression frame_expr, long address)
		{
			this.frame_expr = frame_expr;
			this.address = address;
		}

		public override string Name {
			get { return String.Format ("%0x{0:x}", address); }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			frame = (FrameHandle) frame_expr.Resolve (context);
			if (frame == null)
				return null;

			return frame.Frame.Language.PointerType;
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetType type = DoResolveType (context);
			if (type == null)
				return null;

			TargetAddress taddress = new TargetAddress (frame.Frame.AddressDomain, address);
			location = new AbsoluteTargetLocation (frame.Frame, taddress);

			return type.GetObject (location);
		}
	}

	public class InvocationExpression : VariableExpression
	{
		VariableExpression method_expr;
		Expression[] arguments;

		public InvocationExpression (VariableExpression method_expr, Expression[] arguments)
		{
			this.method_expr = method_expr;
			this.arguments = arguments;
		}

		public override string Name {
			get { return String.Format ("{0} ()", method_expr.Name); }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetFunctionObject func = method_expr.ResolveMethod (context, arguments);

			return func.Type;
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			return Invoke (context, true);
		}

		public ITargetObject Invoke (ScriptingContext context, bool need_retval)
		{
			ITargetFunctionObject func = method_expr.ResolveMethod (context, arguments);

			object[] args = new object [arguments.Length];
			for (int i = 0; i < arguments.Length; i++)
				args [i] = arguments [i].Resolve (context);

			try {
				ITargetObject retval = func.Invoke (args);
				if (need_retval && !func.Type.HasReturnValue)
					throw new ScriptingException ("Method `{0}' doesn't return a value.", Name);

				return retval;
			} catch (MethodOverloadException ex) {
				throw new ScriptingException ("Cannot invoke method `{0}': {1}", Name, ex.Message);
			} catch (TargetInvocationException ex) {
				throw new ScriptingException ("Invocation of `{0}' raised an exception: {1}", Name, ex.Message);
			}
		}
	}
}
