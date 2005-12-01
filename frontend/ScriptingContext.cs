using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Remoting;

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public delegate void ProcessExitedHandler (ProcessHandle handle);

	public class ProcessHandle : MarshalByRefObject
	{
		DebuggerClient client;
		Interpreter interpreter;
		ThreadGroup tgroup;
		Process process;
		string name;
		int id;

		Hashtable registers;

		public ProcessHandle (Interpreter interpreter, DebuggerClient client,
				      Process process)
		{
			this.interpreter = interpreter;
			this.client = client;
			this.process = process;
			this.name = process.Name;
			this.id = process.ID;

			initialize ();
		}

		public ProcessHandle (Interpreter interpreter, DebuggerClient client,
				      Process process, int pid)
			: this (interpreter, client, process)
		{
			if (process.HasTarget) {
				if (!process.IsDaemon) {
					StackFrame frame = process.CurrentFrame;
					current_frame = frame;
					interpreter.Print ("{0} stopped at {1}.", Name, frame);
					interpreter.Style.PrintFrame (
						interpreter.GlobalContext, current_frame);
				}
			}
		}

		public Process Process {
			get { return process; }
		}

		public TargetAccess TargetAccess {
			get { return process.TargetAccess; }
		}

		public ThreadGroup ThreadGroup {
			get { return tgroup; }
		}

		public DebuggerClient DebuggerClient {
			get { return client; }
		}

		public event ProcessExitedHandler ProcessExitedEvent;

		void initialize ()
		{
			registers = new Hashtable ();
			Architecture arch = process.Architecture;

			string[] reg_names = arch.RegisterNames;
			int[] reg_indices = arch.AllRegisterIndices;

			for (int i = 0; i < reg_names.Length; i++) {
				string register = reg_names [i];

				registers.Add (register, reg_indices [i]);
			}

			tgroup = ThreadGroup.CreateThreadGroup ("@" + ID);
			tgroup.AddThread (ID);
		}

		int current_frame_idx = -1;
		StackFrame current_frame = null;
		Backtrace current_backtrace = null;

		protected void ProcessExited ()
		{
			process = null;

			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
		}

		internal void TargetEvent (TargetEventArgs args)
		{
			current_frame = args.Frame;

			current_frame_idx = -1;
			current_backtrace = null;

			switch (args.Type) {
			case TargetEventType.TargetExited:
				if (!process.IsDaemon) {
					if ((int) args.Data == 0)
						interpreter.Print ("{0} terminated normally.", Name);
					else
						interpreter.Print ("{0} exited with exit code {1}.",
								   id, (int) args.Data);
				}
				ProcessExited ();
				break;

			case TargetEventType.TargetSignaled:
				if (!process.IsDaemon) {
					interpreter.Print ("{0} died with fatal signal {1}.",
							   id, (int) args.Data);
				}
				ProcessExited ();
				break;
			}
		}

		public Architecture Architecture {
			get {
				if (process.Architecture == null)
					throw new ScriptingException ("Unknown architecture");

				return process.Architecture;
			}
		}

		public int ID {
			get {
				return id;
			}
		}

		public bool IsAlive {
			get {
				return process != null;
			}
		}

		public int CurrentFrameIndex {
			get {
				if (current_frame_idx == -1)
					return 0;

				return current_frame_idx;
			}

			set {
				GetBacktrace (-1);
				if ((value < 0) || (value >= current_backtrace.Count))
					throw new ScriptingException ("No such frame.");

				current_frame_idx = value;
				current_frame = current_backtrace [current_frame_idx];
			}
		}

		public Backtrace GetBacktrace (int max_frames)
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (!process.IsStopped)
				throw new ScriptingException ("{0} is not stopped.", Name);

			if ((max_frames == -1) && (current_backtrace != null))
				return current_backtrace;

			current_backtrace = process.GetBacktrace (max_frames);

			if (current_backtrace == null)
				throw new ScriptingException ("No stack.");

			return current_backtrace;
		}

		public StackFrame CurrentFrame {
			get {
				return GetFrame (current_frame_idx);
			}
		}

		public StackFrame GetFrame (int number)
		{
			if (State == TargetState.NO_TARGET)
				throw new ScriptingException ("No stack.");
			else if (!process.IsStopped)
				throw new ScriptingException ("{0} is not stopped.", Name);

			if (number == -1) {
				if (current_frame == null)
					current_frame = process.CurrentFrame;

				return current_frame;
			}

			GetBacktrace (-1);
			if (number >= current_backtrace.Count)
				throw new ScriptingException ("No such frame: {0}", number);

			return current_backtrace [number];
		}

		public TargetState State {
			get {
				if (process == null)
					return TargetState.NO_TARGET;
				else
					return process.State;
			}
		}

		public int GetRegisterIndex (string name)
		{
			if (!registers.Contains (name))
				throw new ScriptingException ("No such register: %{0}", name);

			return (int) registers [name];
		}
		
		public void Kill ()
		{
			process.Kill ();
			process = null;
			ProcessExited ();
		}

		public string Name {
			get {
				return name;
			}
		}

		public override string ToString ()
		{
			if (process == null)
				return String.Format ("Zombie @{0}", id);
			else if (process.IsDaemon)
				return String.Format ("Daemon process @{0}: {1} {2}", id, process.PID, State);
			else
				return String.Format ("Process @{0}: {1} {2}", id, process.PID, State);
		}
	}

	public class ScriptingException : Exception
	{
		public ScriptingException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
	}

	public class MultipleLocationsMatchException : ScriptingException
	{
		public readonly SourceMethod[] Sources;
		public MultipleLocationsMatchException (SourceMethod[] sources)
		  : base ("")
		{
			Sources = sources;
		}
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
		ProcessHandle current_process;
		int current_frame_idx = -1;
		Interpreter interpreter;

		ScriptingContext parent;
		ArrayList method_search_results;
		Hashtable method_search_hash;

		bool is_interactive;
		bool is_synchronous;

		internal static readonly string DirectorySeparatorStr;

		static ScriptingContext ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

		internal ScriptingContext (Interpreter interpreter, bool is_interactive,
					   bool is_synchronous)
		{
			this.interpreter = interpreter;
			this.is_interactive = is_interactive;
			this.is_synchronous = is_synchronous;

			method_search_results = new ArrayList ();
			method_search_hash = new Hashtable ();
		}

		protected ScriptingContext (ScriptingContext parent)
			: this (parent.Interpreter, parent.IsInteractive, parent.IsSynchronous)
		{
			this.parent = parent;

			current_process = parent.CurrentProcess;
		}

		public ScriptingContext GetExpressionContext ()
		{
			return new ScriptingContext (this);
		}

		public ScriptingContext Parent {
			get { return parent; }
		}

		public Interpreter Interpreter {
			get { return interpreter; }
		}

		public Debugger GetDebugger ()
		{
			if (current_process == null)
				throw new ScriptingException ("No program to debug.");

			Debugger backend = current_process.Process.Debugger;
			if (backend == null)
				throw new ScriptingException ("No program to debug.");

			return backend;
		}

		public bool HasBackend {
			get {
				return interpreter.DebuggerManager.HasTarget;
			}
		}

		public bool IsInteractive {
			get { return is_interactive; }
		}

		public bool IsSynchronous {
			get { return is_synchronous; }
		}

		public ProcessHandle CurrentProcess {
			get {
				if ((current_process == null) || !current_process.IsAlive)
					throw new ScriptingException ("No program to debug.");

				return current_process;
			}

			set { current_process = value; }
		}

		public StackFrame CurrentFrame {
			get {
				return CurrentProcess.GetFrame (current_frame_idx);
			}
		}

		public int CurrentFrameIndex {
			get {
				return current_frame_idx;
			}

			set {
				current_frame_idx = value;
			}
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
				return interpreter.AddressDomain;
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
			TargetAccess target = CurrentProcess.Process.TargetAccess;
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
				TargetObject retval = CurrentProcess.Process.RuntimeInvoke (
					ftype, obj, new TargetObject [0], true, out exc_message);
				if ((exc_message != null) || (retval == null))
					return null;

				object value = ((TargetFundamentalObject) retval).GetObject (target);
				return String.Format ("({0}) {{ \"{1}\" }}", obj.Type.Name, value);
			}

			cobj = cobj.GetParentObject (target);
			if (cobj != null)
				goto again;

			return null;
		}

		string DoPrintObject (TargetObject obj, DisplayFormat format)
		{
			if (format == DisplayFormat.Object) {
				TargetClassObject cobj = obj as TargetClassObject;
				if (cobj != null) {
					string formatted = MonoObjectToString (cobj);
					if (formatted != null)
						return formatted;
				}
			}

			return CurrentProcess.Process.PrintObject (interpreter.Style, obj, format);
		}

		public void PrintObject (object obj, DisplayFormat format)
		{
			string formatted;
			try {
				if (obj is TargetObject)
					formatted = DoPrintObject ((TargetObject) obj, format);
				else
					formatted = interpreter.Style.FormatObject (
						CurrentProcess.Process.TargetAccess, obj, format);
			} catch {
				formatted = "<cannot display object>";
			}
			Print (formatted);
		}

		public void PrintType (TargetType type)
		{
			string formatted;
			try {
				formatted = CurrentProcess.Process.PrintType (
					interpreter.Style, type);
			} catch {
				formatted = "<cannot display type>";
			}
			Print (formatted);
		}

		public void PrintInstruction (AssemblerLine line)
		{
			if (line.Label != null)
				Print ("{0}:", line.Label);
			Print ("{0:11x}\t{1}", line.Address, line.Text);
		}

		public void AddMethodSearchResult (SourceMethod[] methods, bool print)
		{
			ClearMethodSearchResults ();

			if (print)
				interpreter.Print ("More than one method matches your query:");

			foreach (SourceMethod method in methods) {
				int id = AddMethodSearchResult (method);
				if (print)
					interpreter.Print ("{0,4}  {1}", id, method.Name);
			}

			if (print)
				interpreter.Print ("\nYou may use either the full method signature,\n" +
						   "'-id N' where N is the number to the left of the method, or\n" +
						   "'-all' to select all methods.");
		}

		public void PrintMethods (SourceMethod[] methods)
		{
			foreach (SourceMethod method in methods) {
				int id = AddMethodSearchResult (method);
				interpreter.Print ("{0,4}  {1}", id, method.Name);
			}
		}

		public void PrintMethods (SourceFile source)
		{
			Print ("Methods from source file {0}: {1}", source.ID, source.FileName);
			PrintMethods (source.Methods);
		}

		public int AddMethodSearchResult (SourceMethod method)
		{
			if (method_search_hash.Contains (method.Name))
				return (int) method_search_hash [method.Name];

			int index = method_search_results.Count + 1;
			method_search_hash.Add (method.Name, index);
			method_search_results.Add (method);
			return index;
		}

		public SourceMethod GetMethodSearchResult (int index)
		{
			if ((index < 1) || (index > method_search_results.Count))
				throw new ScriptingException (
					"No such item in the method history.");

			return (SourceMethod) method_search_results [index - 1];
		}

		public int NumMethodSearchResults
		{
			get {
				return method_search_results.Count;
			}
		}

		public void ClearMethodSearchResults ()
		{
			method_search_hash = new Hashtable ();
			method_search_results = new ArrayList ();
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
					buffer = location.SourceBuffer;

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

		public string GetFullPathByFilename (string filename)
		{
			Debugger backend = GetDebugger ();

			try {
				backend.ModuleManager.Lock ();

				Module[] modules = backend.Modules;

				foreach (Module module in modules) {
					if (!module.SymbolsLoaded)
						continue;

					foreach (SourceFile source in module.SymbolFile.Sources) {
						if (filename.Equals (source.Name))
							return source.FileName;
					}
				}
			} finally {
				backend.ModuleManager.UnLock ();
			}

			return null;
		}


		public string GetFullPath (string filename)
		{
			Debugger backend = GetDebugger ();

			if (Path.IsPathRooted (filename))
				return filename;

			string path = GetFullPathByFilename (filename);
			if (path == null)
				path = String.Concat (
					backend.ProcessStart.BaseDirectory, DirectorySeparatorStr,
					filename);

			return path;
		}

		public SourceLocation FindLocation (string file, int line)
		{
			string path = GetFullPath (file);
			Debugger backend = GetDebugger ();
			SourceLocation location = backend.FindLocation (path, line);

			if (location != null)
				return location;
			else
				throw new ScriptingException ("No method contains the specified file/line.");
		}

		public SourceLocation FindLocation (SourceLocation location, int line)
		{
			if (location.HasSourceFile)
				return FindLocation (location.SourceFile.FileName, line);

			if (line > location.SourceBuffer.Contents.Length)
				throw new ScriptingException ("Requested line is outside the current buffer.");

			return new SourceLocation (location.Module, location.SourceBuffer, line);
		}

		public SourceLocation FindMethod (string name)
		{
			Debugger backend = GetDebugger ();
			return backend.FindMethod (name);
		}

		public Module[] GetModules (int[] indices)
		{
			Debugger backend = GetDebugger ();

			try {
				backend.ModuleManager.Lock ();

				int pos = 0;
				Module[] retval = new Module [indices.Length];

				Module[] modules = backend.Modules;

				foreach (int index in indices) {
					if ((index < 0) || (index > modules.Length))
						throw new ScriptingException ("No such module {0}.", index);

					retval [pos++] = modules [index];
				}

				return retval;
			} finally {
				backend.ModuleManager.UnLock ();
			}
		}

		public Module[] Modules 	{
			get {
				Debugger backend = GetDebugger ();
				return backend.Modules;
			}
		}

		public SourceFile[] GetSources (int[] indices)
		{
			Debugger backend = GetDebugger ();

			try {
				backend.ModuleManager.Lock ();

				Hashtable source_hash = new Hashtable ();

				Module[] modules = backend.Modules;

				foreach (Module module in modules) {
					if (!module.SymbolsLoaded)
						continue;

					foreach (SourceFile source in module.SymbolFile.Sources)
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
				backend.ModuleManager.UnLock ();
			}
		}

		public void ShowModules ()
		{
			Debugger backend = GetDebugger ();

			try {
				backend.ModuleManager.Lock ();
				Module[] modules = backend.Modules;

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
				backend.ModuleManager.UnLock ();
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
			Debugger backend = GetDebugger ();

			try {
				backend.ModuleManager.Lock ();

				foreach (Module module in modules)
					module_operation (module, operations);
			} finally {
				backend.ModuleManager.UnLock ();
				backend.SymbolTableManager.Wait ();
			}
		}

		public void ShowSources (Module module)
		{
			if (!module.SymbolsLoaded)
				return;

			Print ("Sources for module {0}:", module.Name);

			foreach (SourceFile source in module.SymbolFile.Sources)
				Print ("{0,4}  {1}", source.ID, source.FileName);
		}

		public ISourceBuffer FindFile (string filename)
		{
			Debugger backend = GetDebugger ();
			return backend.SourceFileFactory.FindFile (filename);
		}

		public void LoadLibrary (Process process, string filename)
		{
			Debugger backend = GetDebugger ();
			string pathname = Path.GetFullPath (filename);
			if (!File.Exists (pathname))
				throw new ScriptingException (
					"No such file: `{0}'", pathname);

			try {
				backend.LoadLibrary (process, pathname);
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Cannot load library `{0}': {1}",
					pathname, ex.Message);
			}

			Print ("Loaded library {0}.", filename);
		}
	}
}

