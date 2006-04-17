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

	public class ScriptingContext : MarshalByRefObject
	{
		Thread current_thread;
		StackFrame current_frame;
		Interpreter interpreter;

		public ScriptingContext (Interpreter interpreter)
		{
			this.interpreter = interpreter;
		}

		public Interpreter Interpreter {
			get { return interpreter; }
		}

		public Process GetProcess ()
		{
			if (current_thread == null)
				throw new TargetException (TargetError.NoTarget);

			Process process = current_thread.Process;
			if (process == null)
				throw new TargetException (TargetError.NoTarget);

			return process;
		}

		public bool HasBackend {
			get {
				return interpreter.HasTarget;
			}
		}

		public Thread CurrentThread {
			get {
				if (current_thread == null)
					throw new TargetException (TargetError.NoTarget);

				return current_thread;
			}

			set { current_thread = value; }
		}

		public StackFrame CurrentFrame {
			get {
				if (current_frame == null)
					throw new TargetException (TargetError.NoTarget);

				return current_frame;
			}

			set { current_frame = value; }
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

		public void Error (string message)
		{
			interpreter.Error (message);
		}

		public void Error (string format, params object[] args)
		{
			interpreter.Error (String.Format (format, args));
		}

		public void Error (ScriptingException ex)
		{
			interpreter.Error (ex);
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

		int last_line = -1;
		string[] current_source_code = null;

		public void ListSourceCode (SourceLocation location, int count)
		{
			int start;

			if ((location == null) && (current_source_code == null))
				location = CurrentLocation;
			if (location == null) {
				if (count < 0){
					start = System.Math.Max (last_line + 2 * count, 0);
					count = -count;
				} else 
					start = last_line;
			} else {
				ISourceBuffer buffer;

				if (location.HasSourceFile) {
					string filename = location.SourceFile.FileName;
					buffer = FindFile (filename);
					if (buffer == null)
						throw new ScriptingException (
							"Cannot find source file `{0}'", filename);
				} else
					throw new ScriptingException (
						"Current location doesn't have any source code.");

				current_source_code = buffer.Contents;

				if (count < 0)
					start = System.Math.Max (location.Line + 2, 0);
				else 
					start = System.Math.Max (location.Line - 2, 0);
			}

			last_line = System.Math.Min (start + count, current_source_code.Length);

			if (start > last_line){
				int t = start;
				start = last_line;
				last_line = t;
			}

			for (int line = start; line < last_line; line++)
				interpreter.Print (String.Format ("{0,4} {1}", line + 1, current_source_code [line]));
		}

		public void ResetCurrentSourceCode ()
		{
			current_source_code = null;
			last_line = -1;
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
			Process process = GetProcess ();
			SourceLocation location = process.FindLocation (path, line);

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
			Process process = GetProcess ();
			return process.FindMethod (name);
		}

		public Module[] GetModules (int[] indices)
		{
			Process process = GetProcess ();

			try {
				process.ModuleManager.Lock ();

				int pos = 0;
				Module[] retval = new Module [indices.Length];

				Module[] modules = process.Modules;

				foreach (int index in indices) {
					if ((index < 0) || (index > modules.Length))
						throw new ScriptingException ("No such module {0}.", index);

					retval [pos++] = modules [index];
				}

				return retval;
			} finally {
				process.ModuleManager.UnLock ();
			}
		}

		public Module[] Modules {
			get {
				Process process = GetProcess ();
				return process.Modules;
			}
		}

		public SourceFile[] GetSources (int[] indices)
		{
			Process process = GetProcess ();

			try {
				process.ModuleManager.Lock ();

				Hashtable source_hash = new Hashtable ();

				Module[] modules = process.Modules;

				foreach (Module module in modules) {
					if (!module.SymbolsLoaded)
						continue;

					foreach (SourceFile source in module.Sources)
						source_hash.Add (source.ID, source);
				}

				int pos = 0;
				SourceFile[] retval = new SourceFile [indices.Length];

				foreach (int index in indices) {
					SourceFile source = (SourceFile) source_hash [index];
					if (source == null)
						throw new ScriptingException (
							"No such source file: {0}", index);

					retval [pos++] = source;
				}

				return retval;
			} finally {
				process.ModuleManager.UnLock ();
			}
		}

		public void ShowModules ()
		{
			Process process = GetProcess ();

			try {
				process.ModuleManager.Lock ();
				Module[] modules = process.Modules;

				Print ("{0,4} {1,5} {2,5} {3}", "Id", "step?", "sym?", "Name");
				for (int i = 0; i < modules.Length; i++) {
					Module module = modules [i];

					Print ("{0,4} {1,5} {2,5} {3}",
					       i,
					       module.StepInto ? "y " : "n ",
					       module.SymbolsLoaded ? "y " : "n ",
					       module.Name);
				}
			} finally {
				process.ModuleManager.UnLock ();
			}
		}

		void module_operation (Module module, ModuleOperation[] operations)
		{
			foreach (ModuleOperation operation in operations) {
				switch (operation) {
				case ModuleOperation.Ignore:
					module.LoadSymbols = false;
					break;
				case ModuleOperation.UnIgnore:
					module.LoadSymbols = true;
					break;
				case ModuleOperation.Step:
					module.StepInto = true;
					break;
				case ModuleOperation.DontStep:
					module.StepInto = false;
					break;
				default:
					throw new InternalError ();
				}
			}
		}

		public void ModuleOperations (Module[] modules, ModuleOperation[] operations)
		{
			Process process = GetProcess ();

			try {
				process.ModuleManager.Lock ();

				foreach (Module module in modules)
					module_operation (module, operations);
			} finally {
				process.ModuleManager.UnLock ();
				process.SymbolTableManager.Wait ();
			}
		}

		public void ShowSources (Module module)
		{
			if (!module.SymbolsLoaded)
				return;

			Print ("Sources for module {0}:", module.Name);

			foreach (SourceFile source in module.Sources)
				Print ("{0,4}  {1}", source.ID, source.FileName);
		}

		public ISourceBuffer FindFile (string filename)
		{
			Process process = GetProcess ();
			return process.SourceFileFactory.FindFile (filename);
		}

		public void LoadLibrary (Thread thread, string filename)
		{
			Process process = GetProcess ();
			string pathname = Path.GetFullPath (filename);
			if (!File.Exists (pathname))
				throw new ScriptingException (
					"No such file: `{0}'", pathname);

			try {
				process.LoadLibrary (thread, pathname);
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Cannot load library `{0}': {1}",
					pathname, ex.Message);
			}

			Print ("Loaded library {0}.", filename);
		}
	}
}

