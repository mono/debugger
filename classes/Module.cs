using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public delegate void ModuleEventHandler (Module module);

	// <summary>
	//   A module is either a shared library (containing unmanaged code) or a dll
	//   (containing managed code).
	//
	//   A module maintains all the breakpoints and controls whether to enter methods
	//   while single-stepping.
	// </summary>
	public abstract class Module
	{
		bool load_symbols;
		bool step_into;

		internal Module ()
		{
			load_symbols = true;
			step_into = true;
		}

		public abstract ILanguage Language {
			get;
		}

		public abstract DebuggerBackend DebuggerBackend {
			get;
		}

		internal abstract ILanguageBackend LanguageBackend {
			get;
		}

		// <summary>
		//   This is the name which should be displayed to the user.
		// </summary>
		public abstract string Name {
			get;
		}

		// <summary>
		//   The full pathname where this module was loaded from.
		//   May only be used while @IsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if IsLoaded was false.
		// </summary>
		public virtual string FullName {
			get {
				return Name;
			}
		}

		// <summary>
		//   Whether the module is currently loaded in memory.
		// </summary>
		public virtual bool IsLoaded {
			get { return true; }
		}

		// <summary>
		//   Whether the module's symbol tables are currently loaded.
		// </summary>
		public abstract bool SymbolsLoaded {
			get;
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
				OnModuleChangedEvent ();
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
				OnModuleChangedEvent ();
			}
		}

		// <summary>
		//   Returns whether this module has debugging info.
		//   Note that this property is initialized when trying to read the debugging
		//   info for the first time.
		// </summary>
		public abstract bool HasDebuggingInfo {
			get;
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
		//   This event is emitted when any other changes are made, such as
		//   modifying the LoadModules or StepInto properties.
		// </summary>
		public event ModuleEventHandler ModuleChangedEvent;

		// <summary>
		//   This event is emitted when adding or removing a breakpoint or
		//   enabling/disabling a breakpoint.
		// </summary>
		public event ModuleEventHandler BreakpointsChangedEvent;

		protected virtual void OnSymbolsLoadedEvent ()
		{
			if (SymbolsLoadedEvent != null)
				SymbolsLoadedEvent (this);
		}

		protected virtual void OnSymbolsUnLoadedEvent ()
		{
			if (SymbolsUnLoadedEvent != null)
				SymbolsUnLoadedEvent (this);
		}

		protected virtual void OnModuleChangedEvent ()
		{
			if (ModuleChangedEvent != null)
				ModuleChangedEvent (this);
		}

		protected internal virtual void OnBreakpointsChangedEvent ()
		{
			if (BreakpointsChangedEvent != null)
				BreakpointsChangedEvent (this);
		}

		public abstract ISymbolFile SymbolFile {
			get;
		}

		// <summary>
		//   Find method @name, which must be a full method name including the
		//   signature (System.DateTime.GetUtcOffset(System.DateTime)).
		// </summary>
		public virtual SourceMethod FindMethod (string name)
		{
			if (!SymbolsLoaded)
				return null;

			return SymbolFile.FindMethod (name);
		}

		// <summary>
		//   Find the method containing line @line in @source_file, which must be
		//   the file's full pathname.
		// </summary>
		public virtual SourceLocation FindLocation (string source_file, int line)
		{
			if (!SymbolsLoaded)
				return null;

			foreach (SourceFile source in SymbolFile.Sources) {
				if (source.FileName != source_file)
					continue;

				return source.FindLine (line);
			}

			return null;
		}

		public abstract TargetAddress SimpleLookup (string name);

		// <summary>
		//   Returns the module's ISymbolTable which can be used to find a method
		//   by its address.  May only be used while @SymbolsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if @SymbolsLoaded was false
		// </summary>
		public abstract ISymbolTable SymbolTable {
			get;
		}

		public abstract ISimpleSymbolTable SimpleSymbolTable {
			get;
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
		internal abstract IDisposable RegisterLoadHandler (Process process,
								   SourceMethod method,
								   MethodLoadedHandler handler,
								   object user_data);

		internal abstract SimpleStackFrame UnwindStack (SimpleStackFrame frame,
								ITargetMemoryAccess memory);

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})",
					      GetType (), Name, IsLoaded, SymbolsLoaded, StepInto,
					      LoadSymbols);
		}
	}
}
