using System;
using Math = System.Math;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontends.Scripting
{
	/// <summary>
	///   This interface controls how things are being displayed to the
	///   user, for instance the current stack frame or variables from
	///   the target.
	/// </summary>
	public interface UserInterface
	{
		string Name {
			get;
		}

		bool IsNative {
			get; set;
		}

		void Reset ();

		void PrintFrame (ScriptingContext context, FrameHandle frame);

		void TargetStopped (ScriptingContext context, FrameHandle frame,
				    AssemblerLine current_insn);

		void ShowVariableType (ITargetType type, string name);

		void PrintVariable (IVariable variable, StackFrame frame);

		string FormatObject (object obj);

		string FormatType (ITargetType type);
	}

	public class UserInterfaceMono : UserInterfaceNative
	{
		public UserInterfaceMono (Interpreter interpreter)
			: base (interpreter)
		{ }

		public override string Name {
			get {
				return "mono";
			}
		}

		public override void Reset ()
		{
			IsNative = false;
		}
	}

	public class UserInterfaceNative : UserInterfaceBase, UserInterface
	{
		public UserInterfaceNative (Interpreter interpreter)
			: base (interpreter)
		{ }

		bool native;

		public virtual string Name {
			get {
				return "native";
			}
		}

		public bool IsNative {
			get { return native; }
			set { native = value; }
		}

		public virtual void Reset ()
		{
			IsNative = true;
		}

		public virtual void PrintFrame (ScriptingContext context, FrameHandle frame)
		{
			context.Print (frame);
			if (!frame.PrintSource (context))
				native = true;
			if (native)
				frame.Disassemble (context);
		}

		public void TargetStopped (ScriptingContext context, FrameHandle frame,
					   AssemblerLine current_insn)
		{
			if (frame != null) {
				if (!frame.PrintSource (context))
					native = true;
			}
			if (native && (current_insn != null))
				context.PrintInstruction (current_insn);
		}

		public string FormatObject (object obj)
		{
			if (obj is long)
				return String.Format ("0x{0:x}", (long) obj);
			else if (obj is string)
				return '"' + (string) obj + '"';
			else if (obj is ITargetType)
				return ((ITargetType) obj).Name;
			else if (obj is ITargetObject)
				return FormatObject ((ITargetObject) obj);
			else
				return obj.ToString ();
		}

		protected string FormatMember (ITargetMemberInfo member, bool is_static)
		{
			if (is_static)
				return String.Format ("    static {0} {1}",
						      member.Type.Name, member.Name);
			else
				return String.Format ("           {0} {1}",
						      member.Type.Name, member.Name);
		}

		protected string FormatProperty (ITargetPropertyInfo prop, bool is_static)
		{
			StringBuilder sb = new StringBuilder (FormatMember (prop, is_static));
			sb.Append (" {");
			if (prop.CanRead)
				sb.Append (" get;");
			if (prop.CanWrite)
				sb.Append (" set;");
			sb.Append (" };\n");
			return sb.ToString ();
		}

		protected string FormatMethod (ITargetMethodInfo method, bool is_static,
					       bool is_ctor)
		{
			StringBuilder sb = new StringBuilder ();
			if (is_ctor)
				sb.Append ("    ctor   ");
			else if (is_static)
				sb.Append ("    static ");
			else
				sb.Append ("           ");

			ITargetFunctionType ftype = method.Type;
			if (!is_ctor) {
				if (ftype.HasReturnValue)
					sb.Append (ftype.ReturnType.Name);
				else
					sb.Append ("void");
				sb.Append (" ");
				sb.Append (method.Name);
				sb.Append (" ");
			}
			sb.Append ("(");
			bool first = true;
			foreach (ITargetType ptype in ftype.ParameterTypes) {
				if (first)
					first = false;
				else
					sb.Append (", ");
				sb.Append (ptype.Name);
			}
			sb.Append (");\n");
			return sb.ToString ();
		}

		public string FormatType (ITargetType type)
		{
			switch (type.Kind) {
			case TargetObjectKind.Array: {
				ITargetArrayType atype = (ITargetArrayType) type;
				return String.Format ("{0} []", atype.ElementType.Name);
			}

			case TargetObjectKind.Class:
			case TargetObjectKind.Struct: {
				ITargetStructType stype = (ITargetStructType) type;
				StringBuilder sb = new StringBuilder ();
				ITargetClassType ctype = type as ITargetClassType;
				if (ctype != null) {
					sb.Append ("class ");
					sb.Append (ctype.Name);
					if (ctype.HasParent) {
						sb.Append (" : ");
						sb.Append (ctype.ParentType.Name);
					}
				} else {
					sb.Append ("struct ");
					sb.Append (stype.Name);
				}
				sb.Append (" {\n");
				foreach (ITargetFieldInfo field in stype.Fields)
					sb.Append (FormatMember (field, false) + ";\n");
				foreach (ITargetFieldInfo field in stype.StaticFields)
					sb.Append (FormatMember (field, true) + ";\n");
				foreach (ITargetPropertyInfo property in stype.Properties)
					sb.Append (FormatProperty (property, false));
				foreach (ITargetPropertyInfo property in stype.StaticProperties)
					sb.Append (FormatProperty (property, true));
				foreach (ITargetMethodInfo method in stype.Methods)
					sb.Append (FormatMethod (method, false, false));
				foreach (ITargetMethodInfo method in stype.StaticMethods)
					sb.Append (FormatMethod (method, true, false));
				foreach (ITargetMethodInfo method in stype.Constructors)
					sb.Append (FormatMethod (method, true, true));

				sb.Append ("}\n");

				return sb.ToString ();
			}

			default:
				return type.Name;
			}
		}

		public string FormatObject (ITargetObject obj)
		{
			switch (obj.Type.Kind) {
			case TargetObjectKind.Array: {
				ITargetArrayObject aobj = (ITargetArrayObject) obj;
				StringBuilder sb = new StringBuilder ("[");
				int lower = aobj.LowerBound;
				int upper = aobj.UpperBound;
				for (int i = lower; i < upper; i++) {
					if (i > lower)
						sb.Append (",");
					sb.Append (FormatObject (aobj [i]));
				}
				sb.Append ("]");
				return sb.ToString ();
			}

			case TargetObjectKind.Class:
			case TargetObjectKind.Struct: {
				ITargetStructObject sobj = (ITargetStructObject) obj;
				StringBuilder sb = new StringBuilder ("{");
				bool first = true;
				ITargetFieldInfo[] fields = sobj.Type.Fields;
				foreach (ITargetFieldInfo field in fields) {
					ITargetObject fobj = sobj.GetField (field.Index);
					if (first)
						first = false;
					else
						sb.Append (", ");
					sb.Append (FormatObject (fobj));
				}
				sb.Append ("}");
				return sb.ToString ();
			}

			default:
				return obj.Print ();
			}
		}

		public void PrintVariable (IVariable variable, StackFrame frame)
		{
			ITargetObject obj = null;
			try {
				obj = variable.GetObject (frame);
			} catch {
			}

			string contents;
			if (obj != null)
				contents = FormatObject (obj);
			else
				contents = "<cannot display object>";
				
			Print ("${0} = ({1}) {2}", variable.Name, variable.Type.Name,
			       contents);
		}

		public void ShowVariableType (ITargetType type, string name)
		{
			Print (type.Name);
		}
	}


	///
	/// Ignore this `user interface' - I need it to debug the debugger.
	///

	public class UserInterfaceMartin : UserInterfaceBase, UserInterface
	{
		public UserInterfaceMartin (Interpreter interpreter)
			: base (interpreter)
		{ }

		public string Name {
			get { return "martin"; }
		}

		bool UserInterface.IsNative {
			get { return true; }
			set { ; }
		}

		public void Reset ()
		{ }

		public void TargetStopped (ScriptingContext context, FrameHandle frame,
				    AssemblerLine current_insn)
		{
			if (current_insn != null)
				context.PrintInstruction (current_insn);

			if (frame != null)
				frame.PrintSource (context);
		}

		public void PrintFrame (ScriptingContext context, FrameHandle frame)
		{
			context.Print (frame);
			frame.Disassemble (context);
			frame.PrintSource (context);
		}

		public void PrintVariable (IVariable variable, StackFrame frame)
		{
			ITargetObject obj = null;
			try {
				obj = variable.GetObject (frame);
			} catch {
			}

			string contents;
			if (obj != null)
				contents = FormatObject (obj);
			else
				contents = "<cannot display object>";
				
			Print ("${0} = ({1}) {2}", variable.Name, variable.Type.Name,
			       contents);
		}

		public string FormatObject (object obj)
		{
			if (obj is long)
				return String.Format ("0x{0:x}", (long) obj);
			else
				return obj.ToString ();
		}

		public string FormatType (ITargetType type)
		{
			return type.ToString ();
		}

		public void ShowVariableType (ITargetType type, string name)
		{
			ITargetArrayType array = type as ITargetArrayType;
			if (array != null)
				Print ("{0} is an array of {1}", name, array.ElementType);

			ITargetClassType tclass = type as ITargetClassType;
			ITargetStructType tstruct = type as ITargetStructType;
			if (tclass != null) {
				if (tclass.HasParent)
					Print ("{0} is a class of type {1} which inherits from {2}",
					       name, tclass.Name, tclass.ParentType);
				else
					Print ("{0} is a class of type {1}", name, tclass.Name);
			} else if (tstruct != null)
				Print ("{0} is a value type of type {1}", name, tstruct.Name);

			if (tstruct != null) {
				foreach (ITargetFieldInfo field in tstruct.Fields)
					Print ("  It has a field `{0}' of type {1}", field.Name,
					       field.Type.Name);
				foreach (ITargetFieldInfo field in tstruct.StaticFields)
					Print ("  It has a static field `{0}' of type {1}", field.Name,
					       field.Type.Name);
				foreach (ITargetFieldInfo property in tstruct.Properties)
					Print ("  It has a property `{0}' of type {1}", property.Name,
					       property.Type.Name);
				foreach (ITargetMethodInfo method in tstruct.Methods) {
					if (method.Type.HasReturnValue)
						Print ("  It has a method: {0} {1}", method.Type.ReturnType.Name, method.FullName);
					else
						Print ("  It has a method: void {0}", method.FullName);
				}
				foreach (ITargetMethodInfo method in tstruct.StaticMethods) {
					if (method.Type.HasReturnValue)
						Print ("  It has a static method: {0} {1}", method.Type.ReturnType.Name, method.FullName);
					else
						Print ("  It has a static method: void {0}", method.FullName);
				}
				foreach (ITargetMethodInfo method in tstruct.Constructors) {
					Print ("  It has a constructor: {0}", method.FullName);
				}
				return;
			}

			Print ("{0} is a {1}", name, type);
		}
	}

	public abstract class UserInterfaceBase
	{
		Interpreter interpreter;

		protected UserInterfaceBase (Interpreter interpreter)
		{
			this.interpreter = interpreter;
		}

		public void Print (string message)
		{
			interpreter.Print (message);
		}

		public void Print (string format, params object[] args)
		{
			interpreter.Print (String.Format (format, args));
		}

		public void Print (object obj)
		{
			interpreter.Print (obj.ToString ());
		}
	}
}
