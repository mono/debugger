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
			return method_expr.EvaluateType (context);
		}

		protected override ITargetObject DoEvaluateVariable (ScriptingContext context)
		{
			return Invoke (context, false);
		}

		protected override SourceLocation DoEvaluateLocation (ScriptingContext context,
								      Expression[] types)
		{
			return method_expr.EvaluateLocation (context, arguments);
		}

		public ITargetObject Invoke (ScriptingContext context, bool debug)
		{
			ITargetFunctionObject func = mg.EvaluateMethod (
				context, context.CurrentFrame.Frame, arguments);

			ITargetObject[] args = new ITargetObject [arguments.Length];
			for (int i = 0; i < arguments.Length; i++)
				args [i] = arguments [i].EvaluateVariable (context);

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
