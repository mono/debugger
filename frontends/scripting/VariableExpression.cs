using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
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
				ITargetType type = ResolveType (context);

				if (!obj.IsValid)
					throw new ScriptingException ("Variable `{0}' is out of scope.", Name);

				if (type.Kind == TargetObjectKind.Fundamental)
					return ((ITargetFundamentalObject) obj).Object;

				// FIXME: how to handle all the other kinds of objects?
				return obj;
			} catch (LocationInvalidException ex) {
				throw new ScriptingException ("Location of variable `{0}' is invalid: {1}",
							      Name, ex.Message);
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

		ITargetFieldInfo get_field (ITargetStructType tstruct)
		{
			foreach (ITargetFieldInfo field in tstruct.Fields)
				if (field.Name == identifier)
					return field;

			throw new ScriptingException ("Variable {0} has no field {1}.", var_expr.Name,
						      identifier);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetStructObject sobj = var_expr.ResolveVariable (context) as ITargetStructObject;
			if (sobj == null)
				throw new ScriptingException ("Variable {0} is not a struct or class type.",
							      var_expr.Name);

			ITargetFieldInfo field = get_field (sobj.Type);
			return sobj.GetField (field.Index);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetStructType tstruct = var_expr.ResolveType (context) as ITargetStructType;
			if (tstruct == null)
				throw new ScriptingException ("Variable {0} is not a struct or class type.",
							      var_expr.Name);

			ITargetFieldInfo field = get_field (tstruct);
			return field.Type;
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
}
