using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public delegate void ModuleEventHandler (Module module);

	internal delegate void MethodLoadedHandler (ITargetMemoryAccess target, SourceMethod method,
						    object user_data);

	internal interface ILoadHandler
	{
		object UserData {
			get;
		}

		void Remove ();
	}

	internal abstract class SymbolFile : MarshalByRefObject
	{
		public abstract string FullName {
			get;
		}

		public abstract Language Language {
			get;
		}

		internal abstract ILanguageBackend LanguageBackend {
			get;
		}

		public abstract bool SymbolsLoaded {
			get;
		}

		public abstract bool HasDebuggingInfo {
			get;
		}

		public abstract SourceFile[] Sources {
			get;
		}

		public abstract SourceMethod[] GetMethods (SourceFile file);

		public abstract Method GetMethod (int domain, long handle);

		public abstract SourceMethod FindMethod (string name);

		public abstract Symbol SimpleLookup (TargetAddress address, bool exact_match);

		public abstract ISymbolTable SymbolTable {
			get;
		}

		internal abstract ILoadHandler RegisterLoadHandler (Thread target,
								    SourceMethod method,
								    MethodLoadedHandler handler,
								    object user_data);

		internal abstract StackFrame UnwindStack (StackFrame last_frame,
							  ITargetMemoryAccess memory);

		internal abstract void OnModuleChanged ();
	}

	// <summary>
	//   A module is either a shared library (containing unmanaged code) or a dll
	//   (containing managed code).
	//
	//   A module maintains all the breakpoints and controls whether to enter methods
	//   while single-stepping.
	// </summary>
	public sealed class Module : MarshalByRefObject
	{
		string name;
		[NonSerialized]
		SymbolFile symfile;
		bool load_symbols;
		bool step_into;
		int id;
		static int next_id;

		internal Module (string name)
		{
			this.name = name;
			this.id = ++next_id;

			load_symbols = true;
			step_into = true;
		}

		internal Module (string name, SymbolFile symfile)
			: this (name)
		{
			this.symfile = symfile;
		}

		internal void LoadModule (SymbolFile symfile)
		{
			this.symfile = symfile;
			OnModuleChanged ();
		}

		public Language Language {
			get { return SymbolFile.Language; }
		}

		internal ILanguageBackend LanguageBackend {
			get { return SymbolFile.LanguageBackend; }
		}

		internal SymbolFile SymbolFile {
			get {
				if (symfile != null)
					return symfile;

				throw new InvalidOperationException ();
			}
		}

		// <summary>
		//   This is the name which should be displayed to the user.
		// </summary>
		public string Name {
			get { return name; }
		}

		// <summary>
		//   The full pathname where this module was loaded from.
		//   May only be used while @IsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if IsLoaded was false.
		// </summary>
		public string FullName {
			get {
				return SymbolFile.FullName;
			}
		}

		// <summary>
		//   Whether the module is currently loaded in memory.
		// </summary>
		public bool IsLoaded {
			get { return symfile != null; }
		}

		// <summary>
		//   Whether the module's symbol tables are currently loaded.
		// </summary>
		public bool SymbolsLoaded {
			get { return IsLoaded && SymbolFile.SymbolsLoaded; }
		}

		// <summary>
		//   Whether to load the module's symbol tables when the module is loaded
		//   into memory.  You may set this to false to completely "ignore" a
		//   module the next time you restart the target; the debugger will
		//   neither step into any of the module's methods nor will you get any
		//   method names or source locations in a backtrace.
		//
		//   Note that setting this to false does not disable any breakpoints -
		//   the debugger will still stop if it its a breakpoint inside this
		//   module, but you'll see nothing but an address in the backtrace and
		//   you won't see any source code.
		// </summary>
		public bool LoadSymbols {
			get {
				return load_symbols;
			}

			set {
				if (load_symbols == value)
					return;

				load_symbols = value;
				OnModuleChanged ();
			}
		}

		// <summary>
		//   Whether to enter this module's methods while single-stepping.
		//   If you set this to false, you will still get full debugging support
		//   if the debugger ever stops in this module, for instance because it
		//   hit a breakpoint or received a signal.
		//
		//   When debugging managed applications, you should set this to false for
		//   `mono' and its shared libraries.  If the application ever crashes
		//   somewhere inside an unmanaged method, you'll get full debugging
		//   information in the backtrace, but the debugger will never enter
		//   unmanaged methods while single-stepping.
		// </summary>
		public bool StepInto {
			get {
				return step_into;
			}

			set {
				if (step_into == value)
					return;

				step_into = value;
				OnModuleChanged ();
			}
		}

		// <summary>
		//   Returns whether this module has debugging info.
		//   Note that this property is initialized when trying to read the debugging
		//   info for the first time.
		// </summary>
		public bool HasDebuggingInfo {
			get { return SymbolFile.HasDebuggingInfo; }
		}

		// <summary>
		//   This event is emitted when the module's symbol tables are loaded.
		// </summary>
		public event ModuleEventHandler SymbolsLoadedEvent;

		// <summary>
		//   This event is emitted when the module's symbol tables are unloaded.
		// </summary>
		public event ModuleEventHandler SymbolsUnLoadedEvent;

		// <summary>
		//   This event is emitted when adding or removing a breakpoint or
		//   enabling/disabling a breakpoint.
		// </summary>
		public event ModuleEventHandler BreakpointsChangedEvent;

		internal void OnSymbolsLoadedEvent ()
		{
			if (SymbolsLoadedEvent != null)
				SymbolsLoadedEvent (this);
		}

		internal void OnSymbolsUnLoadedEvent ()
		{
			if (SymbolsUnLoadedEvent != null)
				SymbolsUnLoadedEvent (this);
		}

		protected void OnModuleChanged ()
		{
			if (symfile != null)
				symfile.OnModuleChanged ();
		}

		protected internal void OnBreakpointsChangedEvent ()
		{
			if (BreakpointsChangedEvent != null)
				BreakpointsChangedEvent (this);
		}

		public SourceFile[] Sources {
			get { return SymbolFile.Sources; }
		}

		public SourceMethod[] GetMethods (SourceFile file)
		{
			return SymbolFile.GetMethods (file);
		}

		public Method GetMethod (int domain, long handle)
		{
			return SymbolFile.GetMethod (domain, handle);
		}

		// <summary>
		//   Find method @name, which must be a full method name including the
		//   signature (System.DateTime.GetUtcOffset(System.DateTime)).
		// </summary>
		public SourceMethod FindMethod (string name)
		{
			return SymbolFile.FindMethod (name);
		}

		// <summary>
		//   Find the method containing line @line in @source_file, which must be
		//   the file's full pathname.
		// </summary>
		public SourceLocation FindLocation (string source_file, int line)
		{
			if (!SymbolsLoaded)
				return null;

			foreach (SourceFile source in Sources) {
				if (source.FileName != source_file)
					continue;

				return source.FindLine (line);
			}

			return null;
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			return SymbolFile.SimpleLookup (address, exact_match);
		}

		// <summary>
		//   Returns the module's ISymbolTable which can be used to find a method
		//   by its address.  May only be used while @SymbolsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if @SymbolsLoaded was false
		// </summary>
		public ISymbolTable SymbolTable {
			get { return SymbolFile.SymbolTable; }
		}

		// <summary>
		//   Registers a delegate to be invoked when the method is loaded.
		//   This is an expensive operation and must not be used in a GUI to get
		//   a notification when the `IsLoaded' field changed.
		//
		//   This is an expensive operation, registering too many load handlers
		//   may slow that target down, so do not use this in the user interface
		//   to get any notifications when a method is loaded or something like
		//   this.  It's just intended to insert breakpoints.
		//
		//   To unregister the delegate, dispose the returned IDisposable.
		//
		//   Throws:
		//     InvalidOperationException - IsDynamic was false or IsLoaded was true
		// </summary>
		internal ILoadHandler RegisterLoadHandler (Thread target,
							   SourceMethod method,
							   MethodLoadedHandler handler,
							   object user_data)
		{
			return SymbolFile.RegisterLoadHandler (target, method, handler, user_data);
		}

		internal StackFrame UnwindStack (StackFrame last_frame,
						 ITargetMemoryAccess memory)
		{
			return SymbolFile.UnwindStack (last_frame, memory);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4}:{5}:{6})",
					      GetType (), id, Name, IsLoaded, SymbolsLoaded, StepInto,
					      LoadSymbols);
		}

		internal sealed class SessionSurrogate : ISerializationSurrogate
		{
			public void GetObjectData (object obj, SerializationInfo info,
						   StreamingContext context)
			{
				Module module = (Module) obj;

				info.AddValue ("type", module.GetType ().Name);
				info.AddValue ("name", module.Name);
				info.AddValue ("load-symbols", module.LoadSymbols);
				info.AddValue ("step-into", module.StepInto);
			}

			public object SetObjectData (object obj, SerializationInfo info,
						     StreamingContext context,
						     ISurrogateSelector selector)
			{
				Process process = (Process) context.Context;

				string name = info.GetString ("name");
				Module module = process.ModuleManager.CreateModule (name);

				module.name = info.GetString ("name");
				module.load_symbols = info.GetBoolean ("load-symbols");
				module.step_into = info.GetBoolean ("step-into");

				return module;
			}
		}
	}
}
