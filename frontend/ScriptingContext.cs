using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public class ScriptingException : Exception
	{
		public ScriptingException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
	}

	public enum ModuleOperation
	{
		Ignore,
		UnIgnore,
		Step,
		DontStep
	}

	public class ScriptingContext : DebuggerMarshalByRefObject
	{
		Thread current_thread;
		Process current_process;
		StackFrame current_frame;
		Interpreter interpreter;

		public ScriptingContext (Interpreter interpreter)
		{
			this.interpreter = interpreter;
		}

		public Interpreter Interpreter {
			get { return interpreter; }
		}

		public bool HasBackend {
			get {
				return interpreter.HasTarget;
			}
		}

		public Process CurrentProcess {
			get {
				if (current_process == null)
					throw new TargetException (TargetError.NoTarget);

				return current_process;
			}

			set {
				current_process = value;
			}
		}

		public Thread CurrentThread {
			get {
				if (current_thread == null)
					throw new TargetException (TargetError.NoTarget);

				return current_thread;
			}

			set {
				current_thread = value;

				if (value != null)
					CurrentProcess = value.Process;
			}
		}

		public StackFrame CurrentFrame {
			get {
				if (current_frame == null)
					throw new TargetException (TargetError.NoTarget);

				return current_frame;
			}

			set {
				current_frame = value;

				if (value != null)
					CurrentThread = value.Thread;
			}
		}

		public StackFrame GetFrame (int number)
		{
			Thread thread = CurrentThread;
			if (!thread.IsStopped)
				throw new TargetException (TargetError.NotStopped);

			if (number == -1)
				return thread.CurrentFrame;

			Backtrace bt = thread.GetBacktrace ();
			if (number >= bt.Count)
				throw new ScriptingException ("No such frame: {0}", number);

			return bt [number];
		}

		public Language CurrentLanguage {
			get {
				StackFrame frame = CurrentFrame;
				if (frame.Language == null)
					throw new ScriptingException (
						"Stack frame has no source language.");

				return frame.Language;
			}
		}

		public string[] GetNamespaces (StackFrame frame)
		{
			Method method = frame.Method;
			if ((method == null) || !method.HasSource)
				return null;

			MethodSource msource = method.Source;
			if (msource.IsDynamic)
				return null;

			return msource.GetNamespaces ();
		}

		public string[] GetNamespaces ()
		{
			return GetNamespaces (CurrentFrame);
		}

		public SourceLocation CurrentLocation {
			get {
				StackFrame frame = CurrentFrame;
				if ((frame.SourceAddress == null) ||
				    (frame.SourceAddress.Location == null))
					throw new ScriptingException (
						"Current location doesn't have source code");

				return frame.SourceAddress.Location;
			}
		}

		public AddressDomain AddressDomain {
			get {
				return CurrentThread.AddressDomain;
			}
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
			interpreter.Print (obj);
		}

		string MonoObjectToString (TargetClassObject obj)
		{
			TargetClassObject cobj = obj;

		again:
			TargetClassType ctype = cobj.Type;
			if ((ctype.Name == "System.Object") || (ctype.Name == "System.ValueType"))
				return null;
			TargetMethodInfo[] methods = ctype.Methods;
			foreach (TargetMethodInfo minfo in methods) {
				if (minfo.Name != "ToString")
					continue;

				TargetFunctionType ftype = minfo.Type;
				if (ftype.ParameterTypes.Length != 0)
					continue;
				if (ftype.ReturnType != ftype.Language.StringType)
					continue;

				string exc_message;
				TargetObject retval = CurrentThread.RuntimeInvoke (
					ftype, obj, new TargetObject [0], true, out exc_message);
				if ((exc_message != null) || (retval == null))
					return null;

				object value = ((TargetFundamentalObject) retval).GetObject (CurrentThread);
				return String.Format ("({0}) {{ \"{1}\" }}", obj.Type.Name, value);
			}

			cobj = cobj.GetParentObject (CurrentThread);
			if (cobj != null)
				goto again;

			return null;
		}

		string DoFormatObject (TargetObject obj, DisplayFormat format)
		{
			if (format == DisplayFormat.Object) {
				TargetClassObject cobj = obj as TargetClassObject;
				if (cobj != null) {
					string formatted = MonoObjectToString (cobj);
					if (formatted != null)
						return formatted;
				}
			}

			return CurrentThread.PrintObject (interpreter.Style, obj, format);
		}

		public string FormatObject (object obj, DisplayFormat format)
		{
			string formatted;
			try {
				if (obj is TargetObject)
					formatted = DoFormatObject ((TargetObject) obj, format);
				else
					formatted = interpreter.Style.FormatObject (
						CurrentThread, obj, format);
			} catch {
				formatted = "<cannot display object>";
			}
			return formatted;
		}

		public string FormatType (TargetType type)
		{
			string formatted;
			try {
				formatted = CurrentThread.PrintType (
					interpreter.Style, type);
			} catch {
				formatted = "<cannot display type>";
			}
			return (formatted);
		}

		public void PrintMethods (SourceMethod[] methods)
		{
			for (int i = 0; i < methods.Length; i++) {
				interpreter.Print ("{0,4}  {1}", i+1, methods [i].Name);
			}
		}

		public void PrintMethods (SourceFile source)
		{
			Print ("Methods from source file {0}: {1}", source.ID, source.FileName);
			PrintMethods (source.Methods);
		}

		public void Dump (object obj)
		{
			if (obj == null)
				Print ("null");
			else if (obj is TargetObject)
				Print (DumpObject ((TargetObject) obj));
			else
				Print ("unknown:{0}:{1}", obj.GetType (), obj);
		}

		public string DumpObject (TargetObject obj)
		{
			return String.Format ("object:{0}", DumpType (obj.Type));
		}

		public string DumpType (TargetType type)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (type.Name);
			sb.Append (":");
			sb.Append (type.HasFixedSize);
			sb.Append (":");
			sb.Append (type.Size);
			sb.Append (":");
			sb.Append (type.Kind);
			sb.Append (" ");

			switch (type.Kind) {
			case TargetObjectKind.Fundamental:
				sb.Append (((TargetFundamentalType) type).FundamentalKind);
				break;

			case TargetObjectKind.Pointer: {
				TargetPointerType ptype = (TargetPointerType) type;
				sb.Append (ptype.IsTypesafe);
				sb.Append (":");
				sb.Append (ptype.HasStaticType);
				if (ptype.HasStaticType) {
					sb.Append (":");
					sb.Append (ptype.StaticType.Name);
				}
				break;
			}

			case TargetObjectKind.Array:
				sb.Append (((TargetArrayType) type).ElementType.Name);
				break;

#if FIXME
			case TargetObjectKind.Alias: {
				TargetTypeAlias alias = (TargetTypeAlias) type;
				sb.Append (alias.TargetName);
				if (alias.TargetType != null) {
					sb.Append (":");
					sb.Append (alias.TargetType.Name);
				}
				break;
			}
#endif

			}

			return sb.ToString ();
		}

		public SourceLocation FindLocation (string file, int line)
		{
			string path = interpreter.GetFullPath (file);
			SourceLocation location = CurrentProcess.FindLocation (path, line);

			if (location != null)
				return location;
			else
				throw new ScriptingException ("No method contains the specified file/line.");
		}

		public SourceLocation FindLocation (SourceLocation location, int line)
		{
			if (!location.HasSourceFile)
				throw new ScriptingException ("Location doesn't have any source code.");

			return FindLocation (location.SourceFile.FileName, line);
		}

		public SourceLocation FindMethod (string name)
		{
			return CurrentProcess.FindMethod (name);
		}

		public void ShowSources (Module module)
		{
			if (!module.SymbolsLoaded)
				return;

			Print ("Sources for module {0}:", module.Name);

			foreach (SourceFile source in module.Sources)
				Print ("{0,4}  {1}", source.ID, source.FileName);
		}

		public SourceBuffer FindFile (string filename)
		{
			return CurrentProcess.SourceFileFactory.FindFile (filename);
		}

		public void LoadLibrary (Thread thread, string filename)
		{
			string pathname = Path.GetFullPath (filename);
			if (!File.Exists (pathname))
				throw new ScriptingException (
					"No such file: `{0}'", pathname);

			try {
				CurrentProcess.LoadLibrary (thread, pathname);
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Cannot load library `{0}': {1}",
					pathname, ex.Message);
			}

			Print ("Loaded library {0}.", filename);
		}
	}
}

