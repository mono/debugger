using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	[Expression("variable_expression", "Variable expression")]
	public abstract class VariableExpression : Expression
	{
		public abstract string Name {
			get;
		}

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

		protected virtual ITargetFunctionObject DoResolveMethod (ScriptingContext context)
		{
			throw new ScriptingException ("Variable is not a method: `{0}'", Name);
		}

		public ITargetFunctionObject ResolveMethod (ScriptingContext context)
		{
			try {
				ITargetFunctionObject retval = DoResolveMethod (context);
				if (retval == null)
					throw new ScriptingException ("Can't resolve variable: `{0}'", Name);

				return retval;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException ("Location of variable {0} is invalid: {1}",
							      Name, ex.Message);
			}
		}

		protected abstract ITargetType DoResolveType (ScriptingContext context);

		public ITargetType ResolveType (ScriptingContext context)
		{
			ITargetType type = DoResolveType (context);
			if (type == null)
				throw new ScriptingException ("Can't get type of variable `{0}'.", Name);

			return type;
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
		VariableExpression var_expr;
		string identifier;

		public StructAccessExpression (VariableExpression var_expr, string identifier)
		{
			this.var_expr = var_expr;
			this.identifier = identifier;
		}

		public override string Name {
			get {
				return String.Format ("{0}.{1}", var_expr.Name, identifier);
			}
		}

		ITargetObject get_field (ITargetStructObject sobj)
		{
			foreach (ITargetFieldInfo field in sobj.Type.Fields)
				if (field.Name == identifier)
					return sobj.GetField (field.Index);

			foreach (ITargetFieldInfo field in sobj.Type.Properties)
				if (field.Name == identifier)
					return sobj.GetProperty (field.Index);

			throw new ScriptingException ("Variable {0} has no field {1}.", var_expr.Name,
						      identifier);
		}

		ITargetType get_field_type (ITargetStructType tstruct)
		{
			foreach (ITargetFieldInfo field in tstruct.Fields)
				if (field.Name == identifier)
					return field.Type;

			foreach (ITargetFieldInfo field in tstruct.Properties)
				if (field.Name == identifier)
					return field.Type;

			throw new ScriptingException ("Variable {0} has no field {1}.", var_expr.Name,
						      identifier);
		}

		ITargetFunctionObject get_method (ITargetStructObject sobj)
		{
		again:
			ITargetFunctionObject match = null;
			foreach (ITargetMethodInfo method in sobj.Type.Methods) {
				if (method.Name != identifier)
					continue;

				if (match != null)
					throw new ScriptingException (
						"Ambiguous method `{0}'; need to use full name", identifier);

				match = sobj.GetMethod (method.Index);
			}

			if (match != null)
				return match;

			ITargetClassObject cobj = sobj as ITargetClassObject;
			if ((cobj != null) && cobj.Type.HasParent) {
				sobj = cobj.Parent;
				goto again;
			}

			throw new ScriptingException ("Variable {0} has no method {1}.", var_expr.Name,
						      identifier);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetStructObject sobj = var_expr.ResolveVariable (context) as ITargetStructObject;
			if (sobj == null)
				throw new ScriptingException ("Variable {0} is not a struct or class type.",
							      var_expr.Name);

			return get_field (sobj);
		}

		protected override ITargetFunctionObject DoResolveMethod (ScriptingContext context)
		{
			ITargetObject obj = var_expr.ResolveVariable (context);
			ITargetStructObject sobj = obj as ITargetStructObject;
			if (sobj == null) {
				ITargetFundamentalObject fobj = obj as ITargetFundamentalObject;
				if (fobj.HasClassObject)
					sobj = fobj.ClassObject;
			}

			if (sobj == null)
				throw new ScriptingException ("Variable {0} is not a struct or class type.",
							      var_expr.Name);

			return get_method (sobj);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetStructType tstruct = var_expr.ResolveType (context) as ITargetStructType;
			if (tstruct == null)
				throw new ScriptingException ("Variable {0} is not a struct or class type.",
							      var_expr.Name);

			return get_field_type (tstruct);
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
			ITargetFunctionObject func = method_expr.ResolveMethod (context);

			return func.Type;
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			return Invoke (context, true);
		}

		public ITargetObject Invoke (ScriptingContext context, bool need_retval)
		{
			ITargetFunctionObject func = method_expr.ResolveMethod (context);

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
			}
		}
	}
}
