using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
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
						   TargetEnumInfo info, bool is_static,
						   Hashtable hash)
		{
			string value = "";
			if (info.HasConstValue)
				value = String.Format (" = {0}", info.ConstValue);
			return String.Format ("{0}   {1}{2}", prefix, info.Name, value);
		}

		protected string FormatMember (string prefix, TargetMemberInfo member,
					       bool is_static, Hashtable hash)
		{
			string tname = member.Type != null ? member.Type.Name : "<unknown type>";
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
					sb.Append (ftype.ReturnType != null ?
						   ftype.ReturnType.Name : "<unknown type>");
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
				StringBuilder sb = new StringBuilder ();
				TargetClassType ctype = (TargetClassType) type;
				if (type.Kind == TargetObjectKind.Struct)
					sb.Append ("struct ");
				else
					sb.Append ("class ");
				if (ctype.Name != null) {
					sb.Append (ctype.Name);
					sb.Append (" ");
				}
				if (ctype.HasParent) {
					TargetStructType parent = ctype.GetParentType (target);
					sb.Append (": ");
					sb.Append (parent.Name);
				}

				sb.Append ("\n" + prefix + "{\n");
				sb.Append (FormatStruct (prefix, ctype, hash));
				sb.Append (prefix + "}");

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
				TargetGenericInstanceType gtype = (TargetGenericInstanceType) type;

				StringBuilder sb = new StringBuilder ();
				if (gtype.ContainerType.Kind == TargetObjectKind.Struct)
					sb.Append ("struct ");
				else
					sb.Append ("class ");

				sb.Append (String.Format ("{0} = ", gtype.Name));

				TargetStructType parent = gtype.ContainerType.GetParentType (target);
				sb.Append (String.Format ("{0}", gtype.ContainerType.Name));
				if (parent != null)
					sb.Append (String.Format (" : {0}", parent.Name));

				sb.Append ("\n" + prefix + "{\n");
				sb.Append (FormatStruct (prefix, gtype.ContainerType, hash));
				sb.Append (prefix + "}");

				retval = sb.ToString ();
				break;
			}

			default:
				retval = type.Name;
				break;
			}

			hash.Remove (type);
			return retval;
		}

		protected void FormatAccessibility (StringBuilder sb, string prefix,
						    TargetMemberAccessibility accessibility)
		{
			switch (accessibility) {
			case TargetMemberAccessibility.Public:
				sb.Append (prefix + "public:\n");
				break;
			case TargetMemberAccessibility.Protected:
				sb.Append (prefix + "protected:\n");
				break;
			case TargetMemberAccessibility.Internal:
				sb.Append (prefix + "internal:\n");
				break;
			default:
				sb.Append (prefix + "private:\n");
				break;
			}
		}

		protected void FormatFields (TargetClassType type, bool is_static,
					     TargetMemberAccessibility accessibility,
					     List<string> members, string prefix, Hashtable hash)
		{
			List<TargetFieldInfo> list = new List<TargetFieldInfo> ();
			foreach (TargetFieldInfo field in type.Fields) {
				if (field.IsStatic != is_static)
					continue;
				if (field.Accessibility != accessibility)
					continue;
				list.Add (field);
			}
			if (list.Count == 0)
				return;

			foreach (TargetFieldInfo field in list)
				members.Add (FormatMember (prefix, field, is_static, hash) + ";\n");
		}

		protected void FormatFields (TargetClassType type,
					     TargetMemberAccessibility accessibility,
					     List<String> members, string prefix, Hashtable hash)
		{
			FormatFields (type, false, accessibility, members, prefix, hash);
			FormatFields (type, true, accessibility, members, prefix, hash);
		}

		protected void FormatProperties (TargetClassType type, bool is_static,
						 TargetMemberAccessibility accessibility,
						 List<string> members, string prefix, Hashtable hash)
		{
			List<TargetPropertyInfo> list = new List<TargetPropertyInfo> ();
			foreach (TargetPropertyInfo property in type.Properties) {
				if (property.IsStatic != is_static)
					continue;
				if (property.Accessibility != accessibility)
					continue;
				list.Add (property);
			}
			if (list.Count == 0)
				return;

			foreach (TargetPropertyInfo property in list)
				members.Add (FormatProperty (prefix, property, is_static, hash));
		}

		protected void FormatProperties (TargetClassType type,
						 TargetMemberAccessibility accessibility,
						 List<string> members, string prefix, Hashtable hash)
		{
			FormatProperties (type, false, accessibility, members, prefix, hash);
			FormatProperties (type, true, accessibility, members, prefix, hash);
		}

		protected void FormatEvents (TargetClassType type, bool is_static,
					     TargetMemberAccessibility accessibility,
					     List<string> members, string prefix, Hashtable hash)
		{
			List<TargetEventInfo> list = new List<TargetEventInfo> ();
			foreach (TargetEventInfo einfo in type.Events) {
				if (einfo.IsStatic != is_static)
					continue;
				if (einfo.Accessibility != accessibility)
					continue;
				list.Add (einfo);
			}
			if (list.Count == 0)
				return;

			foreach (TargetEventInfo einfo in list)
				members.Add (FormatEvent (prefix, einfo, is_static, hash));
		}

		protected void FormatEvents (TargetClassType type,
					     TargetMemberAccessibility accessibility,
					     List<string> members, string prefix, Hashtable hash)
		{
			FormatEvents (type, false, accessibility, members, prefix, hash);
			FormatEvents (type, true, accessibility, members, prefix, hash);
		}

		protected void FormatMethods (TargetClassType type, bool is_ctor, bool is_static,
					      TargetMemberAccessibility accessibility,
					      List<string> members, string prefix, Hashtable hash)
		{
			List<TargetMethodInfo> list = new List<TargetMethodInfo> ();
			TargetMethodInfo[] methods = is_ctor ? type.Constructors : type.Methods;
			foreach (TargetMethodInfo method in methods) {
				if (method.IsStatic != is_static)
					continue;
				if (method.Accessibility != accessibility)
					continue;
				list.Add (method);
			}
			if (list.Count == 0)
				return;

			foreach (TargetMethodInfo method in list)
				members.Add (FormatMethod (prefix, method, is_static, is_ctor, hash));
		}

		protected void FormatMethods (TargetClassType type,
					      TargetMemberAccessibility accessibility,
					      List<string> members, string prefix, Hashtable hash)
		{
			FormatMethods (type, false, false, accessibility, members, prefix, hash);
			FormatMethods (type, true, false, accessibility, members, prefix, hash);
			FormatMethods (type, false, true, accessibility, members, prefix, hash);
			FormatMethods (type, true, true, accessibility, members, prefix, hash);
		}

		protected string FormatStruct (string prefix, TargetClassType type, Hashtable hash)
		{
			StringBuilder sb = new StringBuilder ();

			List<string> public_members = new List<string> ();
			List<string> protected_members = new List<string> ();
			List<string> internal_members = new List<string> ();
			List<string> private_members = new List<string> ();

			FormatFields (type, TargetMemberAccessibility.Public,
				      public_members, prefix, hash);
			FormatFields (type, TargetMemberAccessibility.Protected,
				      protected_members, prefix, hash);
			FormatFields (type, TargetMemberAccessibility.Internal,
				      internal_members, prefix, hash);
			FormatFields (type, TargetMemberAccessibility.Private,
				      private_members, prefix, hash);

			FormatProperties (type, TargetMemberAccessibility.Public,
					  public_members, prefix, hash);
			FormatProperties (type, TargetMemberAccessibility.Protected,
					  protected_members, prefix, hash);
			FormatProperties (type, TargetMemberAccessibility.Internal,
					  internal_members, prefix, hash);
			FormatProperties (type, TargetMemberAccessibility.Private,
					  private_members, prefix, hash);

			FormatEvents (type, TargetMemberAccessibility.Public,
					  public_members, prefix, hash);
			FormatEvents (type, TargetMemberAccessibility.Protected,
					  protected_members, prefix, hash);
			FormatEvents (type, TargetMemberAccessibility.Internal,
					  internal_members, prefix, hash);
			FormatEvents (type, TargetMemberAccessibility.Private,
					  private_members, prefix, hash);

			FormatMethods (type, TargetMemberAccessibility.Public,
				       public_members, prefix, hash);
			FormatMethods (type, TargetMemberAccessibility.Protected,
				       protected_members, prefix, hash);
			FormatMethods (type, TargetMemberAccessibility.Internal,
				       internal_members, prefix, hash);
			FormatMethods (type, TargetMemberAccessibility.Private,
				       private_members, prefix, hash);

			if (public_members.Count > 0) {
				sb.Append (prefix + "public:\n");
				foreach (string text in public_members)
					sb.Append (text);
			}

			if (protected_members.Count > 0) {
				sb.Append (prefix + "protected:\n");
				foreach (string text in protected_members)
					sb.Append (text);
			}

			if (internal_members.Count > 0) {
				sb.Append (prefix + "internal:\n");
				foreach (string text in internal_members)
					sb.Append (text);
			}

			if (private_members.Count > 0) {
				sb.Append (prefix + "private:\n");
				foreach (string text in private_members)
					sb.Append (text);
			}

			return sb.ToString ();
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
