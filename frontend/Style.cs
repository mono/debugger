using System;
using System.Text;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontend
{
	[Serializable]
	public class StyleEmacs : StyleCLI
	{
		public StyleEmacs (Interpreter interpreter)
			: base (interpreter)
		{ }

		public override string Name {
			get {
				return "emacs";
			}
		}

		public override void TargetStopped (Interpreter interpreter, StackFrame frame,
						    AssemblerLine current_insn)
		{
			if (frame == null)
				return;

			if (frame != null && frame.SourceAddress != null)
				Console.WriteLine ("\x1A\x1A{0}:{1}:beg:{2}",
						   frame.SourceAddress.Name, "55" /* XXX */,
						   "0x80594d8" /* XXX */);
		}
	}

	[Serializable]
	public class StyleCLI : StyleBase
	{
		public StyleCLI (Interpreter interpreter)
			: base (interpreter)
		{ }

		bool native;

		public override string Name {
			get {
				return "native";
			}
		}

		public override bool IsNative {
			get { return native; }
			set { native = value; }
		}

		public override void Reset ()
		{
			IsNative = false;
		}

		public override void PrintFrame (ScriptingContext context, StackFrame frame)
		{
			context.Print (frame);
			bool native = false;
			if (!PrintSource (context.Interpreter, frame))
				native = true;
			if (native) {
				AssemblerLine insn = frame.Thread.DisassembleInstruction (
					frame.Method, frame.TargetAddress);

				if (insn != null)
					context.Interpreter.PrintInstruction (insn);
				else
					throw new ScriptingException (
						"Cannot disassemble instruction at address {0}.",
						frame.TargetAddress);
			}
		}

		public override void TargetStopped (Interpreter interpreter, StackFrame frame,
						    AssemblerLine current_insn)
		{
			if (frame != null) {
				if (!PrintSource (interpreter, frame))
					native = true;

				interpreter.ShowDisplays (frame);
			}
			if (native && (current_insn != null))
				interpreter.PrintInstruction (current_insn);
		}

		public override void UnhandledException (Interpreter interpreter, StackFrame frame,
							 AssemblerLine insn)
		{
			TargetStopped (interpreter, frame, insn);
		}

		protected bool PrintSource (Interpreter interpreter, StackFrame frame)
		{
			SourceAddress location = frame.SourceAddress;
			if (location == null)
				return false;

			SourceBuffer buffer;
			if (location.SourceFile != null) {
				string filename = location.SourceFile.FileName;
				buffer = interpreter.SourceFileFactory.FindFile (filename);
			} else
				buffer = location.SourceBuffer;

			if ((buffer == null) || (buffer.Contents == null))
				return false;

			string line = buffer.Contents [location.Row - 1];
			interpreter.Print (String.Format ("{0,4} {1}", location.Row, line));
			return true;
		}

		public override void TargetEvent (Thread thread, TargetEventArgs args)
		{
			if (args.Frame != null)
				TargetEvent (thread, args.Frame, args);

			switch (args.Type) {
			case TargetEventType.TargetExited:
				if ((int) args.Data != 0)
					interpreter.Print ("{0} exited with exit code {1}.",
							   thread.Name, (int) args.Data);
				else
					interpreter.Print ("{0} exited normally.", thread.Name);
				break;

			case TargetEventType.TargetSignaled:
				interpreter.Print ("{0} died with fatal signal {1}.",
						   thread.Name, (int) args.Data);
				break;
			}
		}

		protected void TargetEvent (Thread target, StackFrame frame,
					    TargetEventArgs args)
		{
			switch (args.Type) {
			case TargetEventType.TargetStopped: {
				if ((int) args.Data != 0)
					interpreter.Print ("{0} received signal {1} at {2}.",
							   target.Name, (int) args.Data, frame);
				else if (!interpreter.IsInteractive)
					break;
				else
					interpreter.Print ("{0} stopped at {1}.", target.Name, frame);

				if (interpreter.IsScript)
					break;

				AssemblerLine insn;
				try {
					insn = target.DisassembleInstruction (
						frame.Method, frame.TargetAddress);
				} catch {
					insn = null;
				}

				interpreter.Style.TargetStopped (interpreter, frame, insn);

				break;
			}

			case TargetEventType.TargetHitBreakpoint: {
				if (!interpreter.IsInteractive)
					break;

				interpreter.Print ("{0} hit breakpoint {1} at {2}.",
						   target.Name, (int) args.Data, frame);

				if (interpreter.IsScript)
					break;

				AssemblerLine insn;
				try {
					insn = target.DisassembleInstruction (
						frame.Method, frame.TargetAddress);
				} catch {
					insn = null;
				}

				interpreter.Style.TargetStopped (interpreter, frame, insn);

				break;
			}

			case TargetEventType.Exception:
			case TargetEventType.UnhandledException:
				interpreter.Print ("{0} caught {2}exception at {1}.", target.Name, frame,
						   args.Type == TargetEventType.Exception ?
						   "" : "unhandled ");

				if (interpreter.IsScript)
					break;

				AssemblerLine insn;
				try {
					insn = target.DisassembleInstruction (
						frame.Method, frame.TargetAddress);
				} catch {
					insn = null;
				}

				interpreter.Style.UnhandledException (interpreter, frame, insn);

				break;
			}
		}

		public override string FormatObject (Thread target, object obj,
						     DisplayFormat format)
		{
			ObjectFormatter formatter = new ObjectFormatter (format);
			formatter.Format (target, obj);
			return formatter.ToString ();
		}

		protected string FormatEnumMember (Thread target, string prefix,
						   TargetMemberInfo member, bool is_static,
						   Hashtable hash)
		{
			TargetFieldInfo fi = member as TargetFieldInfo;
			string value = "";
			if (fi.HasConstValue)
				value = String.Format (" = {0}", fi.ConstValue);
			return String.Format ("{0}   {1}{2}", prefix, member.Name, value);
		}

		protected string FormatMember (string prefix, TargetMemberInfo member,
					       bool is_static, Hashtable hash)
		{
			string tname = member.Type.Name;
			TargetFieldInfo fi = member as TargetFieldInfo;
			if ((fi != null) && fi.HasConstValue)
				return String.Format (
					"{0}   const {1} {2} = {3}", prefix, tname, member.Name, fi.ConstValue);
			else if (is_static)
				return String.Format (
					"{0}   static {1} {2}", prefix, tname, member.Name);
			else
				return String.Format (
					"{0}   {1} {2}", prefix, tname, member.Name);
		}

		protected string FormatProperty (string prefix, TargetPropertyInfo prop,
						 bool is_static, Hashtable hash)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (FormatMember (prefix, prop, is_static, hash));
			sb.Append (" {");
			if (prop.CanRead)
				sb.Append (" get;");
			if (prop.CanWrite)
				sb.Append (" set;");
			sb.Append (" };\n");
			return sb.ToString ();
		}

		protected string FormatEvent (string prefix, TargetEventInfo ev,
					      bool is_static, Hashtable hash)
		{
			string tname = ev.Type.Name;
			if (is_static)
				return String.Format (
					"{0}   static event {1} {2};\n", prefix, tname, ev.Name);
			else
				return String.Format (
					"{0}   event {1} {2};\n", prefix, tname, ev.Name);
		}

		protected string FormatMethod (string prefix, TargetMethodInfo method,
					       bool is_static, bool is_ctor, Hashtable hash)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (prefix);
			if (is_ctor)
				if (is_static)
					sb.Append ("   .cctor ");
				else
					sb.Append ("   .ctor ");
			else if (is_static)
				sb.Append ("   static ");
			else
				sb.Append ("   ");

			TargetFunctionType ftype = method.Type;
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
			foreach (TargetType ptype in ftype.ParameterTypes) {
				if (first)
					first = false;
				else
					sb.Append (", ");
				sb.Append (ptype != null ? ptype.Name : "<unknown type>");
			}
			sb.Append (");\n");
			return sb.ToString ();
		}

		public override string FormatType (Thread target, TargetType type)
		{
			return FormatType (target, "", type, null);
		}

		protected string FormatType (Thread target, string prefix,
					     TargetType type, Hashtable hash)
		{
			string retval;

			if (hash == null)
				hash = new Hashtable ();

			if (hash.Contains (type))
				return type.Name;
			else
				hash.Add (type, true);

			switch (type.Kind) {
			case TargetObjectKind.Array: {
				TargetArrayType atype = (TargetArrayType) type;
				retval = atype.Name;
				break;
			}

			case TargetObjectKind.Enum: {
				StringBuilder sb = new StringBuilder ();
				TargetEnumType etype = type as TargetEnumType;
				sb.Append ("enum ");

				if (etype.Name != null)
					sb.Append (etype.Name);

				sb.Append ("\n" + prefix + "{\n");

				foreach (TargetEnumInfo field in etype.Members) {
					sb.Append (FormatEnumMember (target, prefix, field, false, hash));
					if (field != etype.Members[etype.Members.Length - 1])
						sb.Append (",");
					sb.Append ("\n");
				}
				

				sb.Append (prefix + "}");

				retval = sb.ToString ();
				break;
			}

			case TargetObjectKind.Class:
			case TargetObjectKind.Struct: {
				TargetClassType stype = (TargetClassType) type;
				StringBuilder sb = new StringBuilder ();
				TargetClassType ctype = type as TargetClassType;
				if (type.Kind == TargetObjectKind.Struct)
					sb.Append ("struct ");
				else
					sb.Append ("class ");
				if (ctype != null) {
					if (ctype.Name != null) {
						sb.Append (ctype.Name);
						sb.Append (" ");
					}
					if (ctype.HasParent) {
						sb.Append (": ");
						sb.Append (ctype.ParentType.Name);
					}
				} else {
					if (stype.Name != null) {
						sb.Append (stype.Name);
						sb.Append (" ");
					}
				}
				sb.Append ("\n" + prefix + "{\n");
				foreach (TargetFieldInfo field in stype.Fields)
					sb.Append (FormatMember (
							   prefix, field, false, hash) + ";\n");
				foreach (TargetFieldInfo field in stype.StaticFields)
					sb.Append (FormatMember (
							   prefix, field, true, hash) + ";\n");
				foreach (TargetPropertyInfo property in stype.Properties)
					sb.Append (FormatProperty (
							   prefix, property, false, hash));
				foreach (TargetPropertyInfo property in stype.StaticProperties)
					sb.Append (FormatProperty (
							   prefix, property, true, hash));
				foreach (TargetEventInfo ev in stype.Events)
					sb.Append (FormatEvent (
							   prefix, ev, false, hash));
				foreach (TargetEventInfo ev in stype.StaticEvents)
					sb.Append (FormatEvent (
							   prefix, ev, true, hash));
				foreach (TargetMethodInfo method in stype.Methods)
					sb.Append (FormatMethod (
							   prefix, method, false, false, hash));
				foreach (TargetMethodInfo method in stype.StaticMethods)
					sb.Append (FormatMethod (
							   prefix, method, true, false, hash));
				foreach (TargetMethodInfo method in stype.Constructors)
					sb.Append (FormatMethod (
							   prefix, method, false, true, hash));
				foreach (TargetMethodInfo method in stype.StaticConstructors)
					sb.Append (FormatMethod (
							   prefix, method, true, true, hash));

				sb.Append (prefix);
				sb.Append ("}");

				retval = sb.ToString ();
				break;
			}

#if FIXME
			case TargetObjectKind.Alias: {
				TargetTypeAlias alias = (TargetTypeAlias) type;
				string name;
				if (alias.TargetType != null)
					name = FormatType (target, prefix, alias.TargetType, hash);
				else
					name = "<unknown type>";
				retval = String.Format ("typedef {0} = {1}", alias.Name, name);
				break;
			}
#endif

			case TargetObjectKind.GenericInstance: {
				TargetGenericInstanceType ginst = (TargetGenericInstanceType) type;
				retval = FormatType (target, prefix, ginst.ContainerType, hash);
				break;
			}

			default:
				retval = type.Name;
				break;
			}

			hash.Remove (type);
			return retval;
		}

		protected string PrintObject (Thread target, TargetObject obj)
		{
			try {
				return obj.Print (target);
			} catch {
				return "<cannot display object>";
			}
		}

		public override string PrintVariable (TargetVariable variable, StackFrame frame)
		{
			ObjectFormatter formatter = new ObjectFormatter (DisplayFormat.Default);
			formatter.FormatVariable (frame, variable);
			return formatter.ToString ();
		}

		public override string ShowVariableType (TargetType type, string name)
		{
			return type.Name;
		}
	}

	[Serializable]
	public abstract class StyleBase : Style
	{
		protected Interpreter interpreter;

		protected StyleBase (Interpreter interpreter)
		{
			this.interpreter = interpreter;
		}

		public abstract bool IsNative {
			get; set;
		}

		public abstract void Reset ();

		public abstract void PrintFrame (ScriptingContext context, StackFrame frame);

		public abstract void TargetStopped (Interpreter interpreter, StackFrame frame,
						    AssemblerLine current_insn);

		public abstract void UnhandledException (Interpreter interpreter, StackFrame frame,
							 AssemblerLine current_insn);

		public abstract void TargetEvent (Thread thread, TargetEventArgs args);
	}
}
