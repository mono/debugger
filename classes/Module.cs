using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public delegate void ModuleEventHandler (Module module);

	// <summary>
	//   A module is either a shared library (containing unmanaged code) or a dll
	//   (containing managed code).  Modules persist across different invocations of
	//   the target and may also be serialized to disk to store the user's settings.
	//
	//   A module maintains all the breakpoints and controls whether to enter methods
	//   while single-stepping.
	// </summary>
	public abstract class Module : ISerializable
	{
		string name;
		bool load_symbols;
		bool step_into;
		bool backend_loaded;
		Hashtable breakpoints;
		static int next_breakpoint_id = 0;

		protected Module (string name)
		{
			this.name = name;

			breakpoints = new Hashtable ();
			load_symbols = true;
			step_into = true;
		}

		public abstract ILanguageBackend Language {
			get;
		}

		// <summary>
		//   This is the name which should be displayed to the user.
		// </summary>
		public string Name {
			get {
				return name;
			}
		}

		// <summary>
		//   The full pathname where this module was loaded from.
		//   May only be used while @IsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if IsLoaded was false.
		// </summary>
		public abstract string FullName {
			get;
		}

		// <summary>
		//   Whether the module is currently loaded in memory.
		// </summary>
		public abstract bool IsLoaded {
			get;
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
				SymbolsChanged (load_symbols);
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
		//   This is basically a private property.  It is used by the DebuggerBackend
		//   to block the ModuleLoadedEvent from being sent before it created the
		//   SingleSteppingEngine.
		// </summary>
		public bool BackendLoaded {
			get {
				return backend_loaded;
			}

			set {
				if (backend_loaded == value)
					return;
				backend_loaded = value;
				if (backend_loaded) {
					if (IsLoaded)
						OnModuleLoadedEvent ();
				}
			}
		}

		protected abstract void SymbolsChanged (bool loaded);


		// <summary>
		//   This is called by the DebuggerBackend when the target has exited.
		// </summary>
		public virtual void UnLoad ()
		{ }

		// <summary>
		//   This event is emitted when the module is loaded.
		// </summary>
		public event ModuleEventHandler ModuleLoadedEvent;

		// <summary>
		//   This event is emitted when the module is unloaded.
		// </summary>
		public event ModuleEventHandler ModuleUnLoadedEvent;

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

		bool is_loaded = false;
		protected virtual void CheckLoaded ()
		{
			bool new_is_loaded = IsLoaded;

			if (new_is_loaded != is_loaded) {
				is_loaded = new_is_loaded;

				if (is_loaded)
					OnModuleLoadedEvent ();
				else
					OnModuleUnLoadedEvent ();
			}
		}

		protected virtual void OnModuleLoadedEvent ()
		{
			if (!backend_loaded)
				return;

			if (ModuleLoadedEvent != null)
				ModuleLoadedEvent (this);

			foreach (BreakpointHandle handle in breakpoints.Values)
				handle.Enable ();

			OnModuleChangedEvent ();
		}

		protected virtual void OnModuleUnLoadedEvent ()
		{
			foreach (BreakpointHandle handle in breakpoints.Values)
				handle.Disable ();

			sources = null;
			symtab = null;
			backend_loaded = false;

			if (ModuleUnLoadedEvent != null)
				ModuleUnLoadedEvent (this);

			OnModuleChangedEvent ();
		}

		protected virtual void OnSymbolsLoadedEvent ()
		{
			if (!LoadSymbols)
				return;

			symtab = GetSymbolTable ();

			if (SymbolsLoadedEvent != null)
				SymbolsLoadedEvent (this);

			OnModuleChangedEvent ();
		}

		protected virtual void OnSymbolsUnLoadedEvent ()
		{
			sources = null;
			symtab = null;

			if (SymbolsUnLoadedEvent != null)
				SymbolsUnLoadedEvent (this);

			OnModuleChangedEvent ();
		}

		protected virtual void OnModuleChangedEvent ()
		{
			if (ModuleChangedEvent != null)
				ModuleChangedEvent (this);
		}

		protected virtual void OnBreakpointsChangedEvent ()
		{
			if (BreakpointsChangedEvent != null)
				BreakpointsChangedEvent (this);
		}

		// <remarks>
		//   This may be used in a derived class to get a notification when a new
		//   breakpoint is inserted.
		// </remarks>
		protected abstract void AddBreakpoint (BreakpointHandle handle);

		// <remarks>
		//   This may be used in a derived class to get a notification when a
		//   breakpoint is removed.
		// </remarks>
		protected abstract void RemoveBreakpoint (BreakpointHandle handle);

		// <summary>
		//   This must be implemented to actually enable the breakpoint.  It is
		//   called after the method has been loaded - so we know the method's
		//   address and can actually insert a breakpoint instruction.
		//   The implementation may return any arbitrary data which will be passed
		//   as the @data argument to DisableBreakpoint() when disabling the breakpoint.
		// </summary>
		protected abstract object EnableBreakpoint (BreakpointHandle handle, TargetAddress address);

		// <summary>
		//   This must be implemented to actually disable the breakpoint.  It is
		//   called which the method is still being loaded and the target is still
		//   alive.  The @data argument is whatever EnableBreakpoint() returned.
		// </summary>
		protected abstract void DisableBreakpoint (BreakpointHandle handle, object data);

		// <summary>
		//   Registers the breakpoint @breakpoint with this module.  The
		//   breakpoint will be inserted at the first line of method @method.
		//
		//   Returns a breakpoint index which can be passed to RemoveBreakpoint()
		//   to remove the breakpoint.
		// </summary>
		public int AddBreakpoint (Breakpoint breakpoint, SourceMethodInfo method)
		{
			return AddBreakpoint (breakpoint, method, 0);
		}

		// <summary>
		//   Registers the breakpoint @breakpoint with this module.  The
		//   breakpoint will be inserted at line @line (counted from the beginning
		//   of the source file) in method @method.
		//
		//   Returns a breakpoint index which can be passed to RemoveBreakpoint()
		//   to remove the breakpoint.
		// </summary>
		public int AddBreakpoint (Breakpoint breakpoint, SourceMethodInfo method, int line)
		{
			int index = ++next_breakpoint_id;
			BreakpointHandle handle = new BreakpointHandle (
				this, breakpoint, method, line, index);
			breakpoints.Add (index, handle);
			OnBreakpointsChangedEvent ();
			return index;
		}

		// <summary>
		//   Removes a breakpoint which has been inserted by AddBreakpoint().
		// </summary>
		public void RemoveBreakpoint (int index)
		{
			if (!breakpoints.Contains (index))
				return;

			BreakpointHandle handle = (BreakpointHandle) breakpoints [index];
			handle.Dispose ();
			breakpoints.Remove (index);
			OnBreakpointsChangedEvent ();
		}

		// <summary>
		//   Returns all breakpoints which have been registered with this module.
		// </summary>
		public Breakpoint[] Breakpoints {
			get {
				ArrayList list = new ArrayList ();
				foreach (BreakpointHandle handle in breakpoints.Values)
					list.Add (handle.Breakpoint);
				Breakpoint[] retval = new Breakpoint [list.Count];
				list.CopyTo (retval, 0);
				return retval;
			}
		}

		SourceInfo[] sources = null;
		ISymbolTable symtab = null;

		// <remarks>
		//   This is called from the SymbolTableManager's background thread when
		//   the module is changed.  It creates a hash table which maps a method
		//   name to a SourceMethodInfo and a list of SourceMethodInfo's which is
		//   sorted by the method's start line.
		// </remarks>
		protected internal virtual void ReadModuleData ()
		{
			lock (this) {
				if (!LoadSymbols)
					return;

				if (sources != null)
					return;

				sources = GetSources ();
			}
		}

		protected abstract SourceInfo[] GetSources ();

		// <summary>
		//   Returns a list of all source files in this method.
		//   May only be used while @SymbolsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if @SymbolsLoaded was false.
		// </summary>
		public SourceInfo[] Sources {
			get {
				if (sources != null)
					return sources;

				return new SourceInfo [0];
			}
		}

		// <summary>
		//   Find method @name, which must be a full method name including the
		//   signature (System.DateTime.GetUtcOffset(System.DateTime)).
		// </summary>
		public virtual SourceMethodInfo FindMethod (string name)
		{
			if (!SymbolsLoaded)
				return null;

			ReadModuleData ();

			foreach (SourceInfo source in Sources) {
				SourceMethodInfo method = source.FindMethod (name);

				if (method != null)
					return method;
			}

			return null;
		}

		// <summary>
		//   Find the method containing line @line in @source_file, which must be
		//   the file's full pathname.
		// </summary>
		public virtual SourceMethodInfo FindMethod (string source_file, int line)
		{
			if (!SymbolsLoaded)
				return null;

			foreach (SourceInfo source in Sources) {
				if (source.FileName != source_file)
					continue;

				return source.FindMethod (line);
			}

			return null;
		}

		// <summary>
		//   Returns the module's ISymbolTable which can be used to find a method
		//   by its address.  May only be used while @SymbolsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if @SymbolsLoaded was false
		// </summary>
		public ISymbolTable SymbolTable {
			get {
				if (!SymbolsLoaded)
					throw new InvalidOperationException ();

				if (symtab != null)
					return symtab;

				symtab = GetSymbolTable ();
				return symtab;
			}
		}

		protected abstract ISymbolTable GetSymbolTable ();

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})",
					      GetType (), Name, IsLoaded, SymbolsLoaded, StepInto,
					      LoadSymbols);
		}

		// <summary>
		//   This is an internal handle to a breakpoint.  It holds the target
		//   specific data for the breakpoint.
		// </summary>
		protected sealed class BreakpointHandle : IDisposable {
			public readonly int Index;
			public readonly int Line;
			public readonly Module Module;
			public readonly Breakpoint Breakpoint;
			public readonly string MethodName;
			public SourceMethodInfo Method;

			public BreakpointHandle (Module module, Breakpoint breakpoint,
						 SourceMethodInfo method, int line, int index)
			{
				this.Module = module;
				this.Breakpoint = breakpoint;
				this.Index = index;
				this.Line = line;
				this.Method = method;
				this.MethodName = method.Name;

				this.Breakpoint.BreakpointChangedEvent += new BreakpointEventHandler (
					breakpoint_changed);

				Module.ModuleUnLoadedEvent += new ModuleEventHandler (module_unloaded);
				Module.ModuleLoadedEvent += new ModuleEventHandler (module_loaded);

				Module.AddBreakpoint (this);
				Enable ();
			}

			// <remarks>
			//   When the module is unloaded, clear the method.
			// </remarks>
			void module_unloaded (Module module)
			{
				Method = null;
			}

			// <summary>
			//   When the module is loaded, search breakpoint's method.
			// </summary>
			void module_loaded (Module module)
			{
				Method = Module.FindMethod (MethodName);
			}

			// <summary>
			//   The method has just been loaded, lookup the breakpoint
			//   address and actually insert it.
			// </summary>
			void method_loaded (SourceMethodInfo method, object user_data)
			{
				load_handler = null;

				TargetAddress address = get_address ();
				if (address.IsNull)
					return;

				handle = Module.EnableBreakpoint (this, address);
				enabled = handle != null;
			}

			TargetAddress get_address ()
			{
				TargetAddress address;
				if (Line != 0)
					address = Method.Lookup (Line);
				else
					address = Method.Method.StartAddress;

				if (address.IsNull)
					Console.WriteLine ("WARNING: Cannot insert breakpoint {0}!",
							   Breakpoint);

				return address;
			}

			// <summary>
			//   This is called via the Breakpoint.BreakpointChangedEvent to
			//   actually enable the breakpoint.
			// </summary>
			public void Enable ()
			{
				// `enabled' specifies whether the breakpoint is actually
				// inserted (ie. there's actually a breakpoint instruction
				// in the target).
				if (enabled || !Module.IsLoaded || !Breakpoint.Enabled) {
					Module.OnBreakpointsChangedEvent ();
					return;
				}

				if (Method.IsLoaded) {
					// The method is already loaded into memory, just
					// lookup the address and insert the breakpoint.
					TargetAddress address = get_address ();
					if (!address.IsNull)
						handle = Module.EnableBreakpoint (this, address);
					if (handle != null)
						enabled = true;
				} else if (Method.IsDynamic) {
					// A dynamic method is a method which may emit a
					// callback when it's loaded.  We register this
					// callback here and do the actual insertion when
					// the method is loaded.
					load_handler = Method.RegisterLoadHandler (
						new MethodLoadedHandler (method_loaded), null);
					if (load_handler != null)
						enabled = true;
				}

				// This is just to inform the GUI that Breakpoint.Enabled
				// has changed.
				Module.OnBreakpointsChangedEvent ();
			}

			// <summary>
			//   This is called via the Breakpoint.BreakpointChangedEvent to
			//   actually disable the breakpoint.
			// </summary>
			public void Disable ()
			{
				if (!enabled) {
					Module.OnBreakpointsChangedEvent ();
					return;
				}

				if (load_handler != null) {
					// We registered a load handler for this
					// breakpoint, but the user requested to disable
					// the breakpoint before the method was ever loaded.
					load_handler.Dispose ();
					load_handler = null;
				} else {
					// The method is actually loaded in memory, so
					// remove the breakpoint instruction in the target.
					Module.DisableBreakpoint (this, handle);
					handle = null;
				}

				enabled = false;

				// This is just to inform the GUI that Breakpoint.Enabled
				// has changed.
				Module.OnBreakpointsChangedEvent ();
			}

			public override string ToString ()
			{
				return String.Format ("Breakpoint ({0}:{1}:{2}:{3})",
						      Index, Breakpoint, Method, enabled);
			}

			IDisposable load_handler;
			object handle;
			bool enabled;

			// <summary>
			//   This is called via the Breakpoint.BreakpointChangedEvent to
			//   actually enable/disable the breakpoint.
			// </summary>
			void breakpoint_changed (Breakpoint breakpoint)
			{
				if (Breakpoint.Enabled)
					Enable ();
				else
					Disable ();
			}

			//
			// IDisposable
			//

			private bool disposed = false;

			private void check_disposed ()
			{
				if (disposed)
					throw new ObjectDisposedException ("BreakpointHandle");
			}

			// <summary>
			//   This instance may hold an actual breakpoint instruction in
			//   the target, so we must make sure it's removed when we're
			//   being Disposed.
			// </summary>
			protected virtual void Dispose (bool disposing)
			{
				if (!this.disposed) {
					if (disposing) {
						if (enabled)
							Module.DisableBreakpoint (this, handle);
						Module.RemoveBreakpoint (this);
					}

					this.disposed = true;

					lock (this) {
						// Nothing to do yet.
					}
				}
			}

			public void Dispose ()
			{
				Dispose (true);
				// Take yourself off the Finalization queue
				GC.SuppressFinalize (this);
			}

			~BreakpointHandle ()
			{
				Dispose (false);
			}
		}

		//
		// ISerializable
		//

		public virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("name", Name);
			info.AddValue ("load_symbols", LoadSymbols);
			info.AddValue ("step_into", StepInto);
		}

		protected Module (SerializationInfo info, StreamingContext context)
		{
			name = info.GetString ("name");
			load_symbols = info.GetBoolean ("load_symbols");
			step_into = info.GetBoolean ("step_into");
		}
	}
}
