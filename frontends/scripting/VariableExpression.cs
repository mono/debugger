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

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			return expr.ResolveType (context);
		}
		
		protected override object DoResolve (ScriptingContext context)
		{
			return ResolveType (context);
		}
	}

	[Expression("variable_expression", "Variable expression")]
	public abstract class VariableExpression : Expression
	{
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

		protected override bool DoAssign (ScriptingContext context, object obj)
		{
			ITargetObject target_object = ResolveVariable (context);

			ITargetStructObject sobj = target_object as ITargetStructObject;
			if (sobj != null) {
				ITargetStructObject tobj = obj as ITargetStructObject;
				string kind = sobj.Type.Kind == TargetObjectKind.Class ? "class" : "struct";
				if (tobj == null)
					throw new ScriptingException (
						"Type mismatch: cannot assign non-{0} object to {1} variable {2}.",
						kind, kind, Name);

				if (sobj.Type != tobj.Type)
					throw new ScriptingException (
						"Type mismatch: cannot assign expression of type `{0}' to variable {1}, " +
						"which is of type `{2}'.", tobj.Type.Name, Name, sobj.Type.Name);
			}

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

			return true;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), Name);
		}
	}

	public abstract class PointerExpression : VariableExpression
	{
		public new abstract TargetLocation ResolveLocation (ScriptingContext context);
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
		}

		public override string Name {
			get { return '%' + name; }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			register = frame.FindRegister (name);
			return frame.GetRegisterType (register);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;
			register = frame.FindRegister (name);
			return context.CurrentFrame.GetRegister (register, offset);
		}

		public override TargetLocation ResolveLocation (ScriptingContext context)
		{
			ResolveBase (context);
			FrameHandle frame = context.CurrentFrame;
			register = frame.FindRegister (name);
			return frame.GetRegisterLocation (register, offset, true);
		}

		protected override bool DoAssign (ScriptingContext context, object obj)
		{
			if (offset != 0)
				throw new ScriptingException (
					"Cannot assign a register expression which " +
					"has an offset.");

			long value = Convert.ToInt64 (obj);
			context.CurrentFrame.SetRegister (register, value);
			return true;
		}
	}

	public class StructAccessExpression : VariableExpression
	{
		string name;

		public readonly string Identifier;
		public readonly bool IsStatic;

		new ITargetStructType Type;
		ITargetStructObject Instance;
		StackFrame Frame;

		public StructAccessExpression (StackFrame frame, ITargetStructType type,
					       string identifier)
		{
			this.Frame = frame;
			this.Type = type;
			this.Identifier = identifier;
			this.IsStatic = true;
		}

		public StructAccessExpression (StackFrame frame, ITargetStructObject instance,
					       string identifier)
		{
			this.Frame = frame;
			this.Type = instance.Type;
			this.Instance = instance;
			this.Identifier = identifier;
			this.IsStatic = false;
		}

		public override string Name {
			get {
				return Identifier;
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

		public static ITargetMethodInfo OverloadResolve (ScriptingContext context,
								 ILanguage language,
								 ITargetStructType stype,
								 Expression[] types,
								 ArrayList candidates)
		{
			// We do a very simple overload resolution here
			ITargetType[] argtypes = new ITargetType [types.Length];
			for (int i = 0; i < types.Length; i++)
				argtypes [i] = types [i].ResolveType (context);

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

		protected ITargetMethodInfo FindMethod (ScriptingContext context, ITargetStructType stype, Expression[] types)
		{
		again:
			bool found_match = false;
			ArrayList candidates = new ArrayList ();

			if (!IsStatic) {
				foreach (ITargetMethodInfo method in stype.Methods) {
					if (method.Name != Identifier)
						continue;

					if ((types != null) && (method.Type.ParameterTypes.Length != types.Length)) {
						found_match = true;
						continue;
					}

					candidates.Add (method);
				}
			}

			foreach (ITargetMethodInfo method in stype.StaticMethods) {
				if (method.Name != Identifier)
					continue;

				if ((types != null) && (method.Type.ParameterTypes.Length != types.Length)) {
					found_match = true;
					continue;
				}

				candidates.Add (method);
			}

			if (candidates.Count == 1)
				return (ITargetMethodInfo) candidates [0];

			if (candidates.Count > 1) {
				ITargetMethodInfo retval = null;
				if (types != null)
					retval = OverloadResolve (
						context, Frame.Language, stype, types,
						candidates);
				if (retval == null)
					throw new ScriptingException ("Ambiguous method `{0}'; need to use full name", Name);
				return retval;
			}

			ITargetClassType ctype = stype as ITargetClassType;
			if ((ctype != null) && ctype.HasParent) {
				stype = ctype.ParentType;
				goto again;
			}

			if (found_match && (types != null))
				throw new ScriptingException ("No overload of method `{0}' has {1} arguments.",
							      Name, types.Length);

			return null;
		}

		protected ITargetMemberInfo ResolveTypeBase (ScriptingContext context, bool report_error)
		{
			ITargetMemberInfo member = FindMember (Type, IsStatic, Identifier);
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

			if (member.IsStatic)
				return GetStaticMember (Type, Frame, member);
			else if (!IsStatic)
				return GetMember (Instance, member);
			else
				throw new ScriptingException ("Instance member {0} cannot be used in static context.", Name);
		}

		protected override ITargetFunctionObject DoResolveMethod (ScriptingContext context, Expression[] types)
		{
			ITargetMemberInfo member = ResolveTypeBase (context, false);
			if (member != null)
				throw new ScriptingException ("Member {0} of type {1} is not a method.", Identifier, Type.Name);

			ITargetMethodInfo method;
			if (Identifier.IndexOf ('(') != -1)
				method = FindMethod (context, Type, null);
			else
				method = FindMethod (context, Type, types);

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

		protected override SourceLocation DoResolveLocation (ScriptingContext context,
								     Expression[] types)
		{
			ITargetMemberInfo member = ResolveTypeBase (context, false);
			if (member != null)
				throw new ScriptingException ("Member {0} of type {1} is not a method.", Identifier, Type.Name);

			ITargetMethodInfo method;
			if (Identifier.IndexOf ('(') != -1)
				method = FindMethod (context, Type, null);
			else
				method = FindMethod (context, Type, types);

			if (method != null)
				return new SourceLocation (method.Type.Source);

			if (IsStatic)
				throw new ScriptingException ("Type {0} has no static method {1}.", Type.Name, Identifier);
			else
				throw new ScriptingException ("Type {0} has no method {1}.", Type.Name, Identifier);
		}
	}

	public class PointerDereferenceExpression : PointerExpression
	{
		Expression expr;

		public PointerDereferenceExpression (Expression expr)
		{
			this.expr = expr;
		}

		public override string Name {
			get {
				return '*' + expr.Name;
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetPointerObject pobj = expr.ResolveVariable (context) as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException (
					"Variable `{0}' is not a pointer type.", expr.Name);

			if (!pobj.HasDereferencedObject)
				throw new ScriptingException (
					"Cannot dereference `{0}'.", expr.Name);

			return pobj.DereferencedObject;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetPointerObject pobj = expr.ResolveVariable (context) as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException (
					"Variable `{0}` is not a pointer type.", expr.Name);

			ITargetType type = pobj.CurrentType;
			if (type == null)
				throw new ScriptingException (
					"Cannot get current type of `{0}'.", expr.Name);

			return type;
		}

		public override TargetLocation ResolveLocation (ScriptingContext context)
		{
			ResolveBase (context);
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
					"Variable `{0}' is not a pointer type.", expr.Name);

			return pobj.Location;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			FrameHandle frame = context.CurrentFrame;

			object obj = expr.Resolve (context);
			if (obj is int)
				obj = (long) (int) obj;
			if (obj is long) {
				ITargetType type = frame.Frame.Language.PointerType;

				TargetAddress taddress = new TargetAddress (
					frame.Frame.AddressDomain, (long) obj);

				TargetLocation location = new AbsoluteTargetLocation (
					frame.Frame, taddress);

				return type.GetObject (location);
			}

			ITargetPointerObject pobj = obj as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException (
					"Variable `{0}` is not a pointer type.", expr.Name);

			if (!pobj.HasDereferencedObject)
				throw new ScriptingException (
					"Cannot dereference `{0}'.", expr.Name);

			return pobj.DereferencedObject;
		}
	}

	public class ArrayAccessExpression : VariableExpression
	{
		Expression expr, index;

		public ArrayAccessExpression (Expression expr, Expression index)
		{
			this.expr = expr;
			this.index = index;
		}

		public override string Name {
			get {
				return String.Format ("{0}[{1}]", expr.Name, index);
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			int i;

			ITargetArrayObject obj = expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", expr.Name);
			try {
				i = (int) this.index.Resolve (context);
			} catch (Exception e) {
				throw new ScriptingException (
					"Cannot convert {0} to an integer for indexing: {1}", this.index, e);
			}

			if ((i < obj.LowerBound) || (i >= obj.UpperBound))
				throw new ScriptingException (
					"Index {0} of array expression {1} out of bounds " +
					"(must be between {2} and {3}).", i, expr.Name,
					obj.LowerBound, obj.UpperBound - 1);

			return obj [i];
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetArrayType type = expr.ResolveType (context) as ITargetArrayType;
			if (type == null)
				throw new ScriptingException ("Variable {0} is not an array type.",
							      expr.Name);

			return type.ElementType;
		}
	}

	public class ParentClassExpression : VariableExpression
	{
		Expression expr;

		public ParentClassExpression (Expression expr)
		{
			this.expr = expr;
		}

		public override string Name {
			get {
				return String.Format ("parent ({0})", expr.Name);
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetClassObject obj = expr.ResolveVariable (context) as ITargetClassObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not a class type.", expr.Name);

			if (!obj.Type.HasParent)
				throw new ScriptingException (
					"Variable {0} doesn't have a parent type.",
					expr.Name);

			return obj.Parent;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
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

	public class InvocationExpression : VariableExpression
	{
		Expression method_expr;
		Expression[] arguments;

		public InvocationExpression (Expression method_expr, Expression[] arguments)
		{
			this.method_expr = method_expr;
			this.arguments = arguments;
		}

		public override string Name {
			get { return String.Format ("{0} ()", method_expr.Name); }
		}

		ITargetFunctionObject func;

		protected override bool DoResolveBase (ScriptingContext context)
		{
			func = method_expr.ResolveMethod (context, arguments);
			return func != null;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			return func.Type;
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			return Invoke (context, false);
		}

		protected override ITargetFunctionObject DoResolveMethod (ScriptingContext context, Expression[] types)
		{
			return method_expr.ResolveMethod (context, types);
		}

		protected override SourceLocation DoResolveLocation (ScriptingContext context,
								     Expression[] types)
		{
			return method_expr.ResolveLocation (context, arguments);
		}

		public ITargetObject Invoke (ScriptingContext context, bool debug)
		{
			ResolveBase (context);

			ITargetObject[] args = new ITargetObject [arguments.Length];
			for (int i = 0; i < arguments.Length; i++)
				args [i] = arguments [i].ResolveVariable (context);

			try {
				ITargetObject retval = func.Invoke (args, debug);
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

	public class NewExpression : VariableExpression
	{
		Expression type_expr;
		Expression[] arguments;

		public NewExpression (Expression type_expr, Expression[] arguments)
		{
			this.type_expr = type_expr;
			this.arguments = arguments;
		}

		public override string Name {
			get { return String.Format ("new {0} ()", type_expr.Name); }
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			return type_expr.ResolveType (context);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			return Invoke (context, false);
		}

		public ITargetObject Invoke (ScriptingContext context, bool debug)
		{
			FrameHandle frame = context.CurrentFrame;

			ITargetStructType stype = type_expr.ResolveType (context) as ITargetStructType;
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
				method = StructAccessExpression.OverloadResolve (
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
				args [i] = arguments [i].ResolveVariable (context);

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
}
