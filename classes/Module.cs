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
	[Serializable]
	public class Module : ISerializable, IDeserializationCallback
	{
		string name;
		bool load_symbols;
		bool step_into;
		bool backend_loaded;
		ModuleData module_data;
		Hashtable breakpoints = new Hashtable ();

		internal Module (string name)
		{
			this.name = name;

			load_symbols = true;
			step_into = true;
			initialized = true;
		}

		public ModuleData ModuleData {
			get {
				return module_data;
			}

			set {
				lock (this) {
					module_data = value;
					if (module_data != null) {
						set_module_data (module_data);
						OnModuleLoadedEvent ();
					} else {
						OnModuleUnLoadedEvent ();
					}
				}
			}
		}

		public object Language {
			get {
				lock (this) {
					if (module_data == null)
						throw new InvalidOperationException ();

					return module_data.Language;
				}
			}
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
		public string FullName {
			get {
				lock (this) {
					if (module_data == null)
						throw new InvalidOperationException ();

					return module_data.FullName;
				}
			}
		}

		// <summary>
		//   Whether the module is currently loaded in memory.
		// </summary>
		public bool IsLoaded {
			get { return module_data != null; }
		}

		// <summary>
		//   Whether the module's symbol tables are currently loaded.
		// </summary>
		public bool SymbolsLoaded {
			get {
				lock (this) {
					if (module_data == null)
						return false;

					return module_data.SymbolsLoaded;
				}
			}
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

		// <summary>
		//   Returns whether this module has debugging info.
		//   Note that this property is initialized when trying to read the debugging
		//   info for the first time.
		// </summary>
		public bool HasDebuggingInfo {
			get {
				lock (this) {
					if (module_data == null)
						return false;

					return module_data.HasDebuggingInfo;
				}
			}
		}

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

		protected virtual void OnModuleLoadedEvent ()
		{
			if (!backend_loaded)
				return;

			if (ModuleLoadedEvent != null)
				ModuleLoadedEvent (this);

			OnModuleChangedEvent ();
		}

		protected virtual void OnModuleUnLoadedEvent ()
		{
			backend_loaded = false;

			if (ModuleUnLoadedEvent != null)
				ModuleUnLoadedEvent (this);

			OnModuleChangedEvent ();
		}

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

		void symbols_loaded (ModuleData data)
		{
			OnSymbolsLoadedEvent ();
		}

		void symbols_unloaded (ModuleData data)
		{
			OnSymbolsUnLoadedEvent ();
		}

		void set_module_data (ModuleData data)
		{
			data.SymbolsLoadedEvent += new ModuleDataEventHandler (symbols_loaded);
			data.SymbolsUnLoadedEvent += new ModuleDataEventHandler (symbols_unloaded);
		}

		// <summary>
		//   Registers the breakpoint @breakpoint with this module.  The
		//   breakpoint will be inserted at the first line of method @method.
		//
		//   Returns a breakpoint index which can be passed to RemoveBreakpoint()
		//   to remove the breakpoint.
		// </summary>
		public int AddBreakpoint (Breakpoint breakpoint, ThreadGroup group,
					  SourceLocation location)
		{
			return AddBreakpoint (new BreakpointHandle (breakpoint, this, group, location));
		}

		protected int AddBreakpoint (BreakpointHandle handle)
		{
			lock (this) {
				int index = handle.Breakpoint.Index;
				breakpoints.Add (index, handle);
				OnBreakpointsChangedEvent ();
				return index;
			}
		}

		// <summary>
		//   Removes a breakpoint which has been inserted by AddBreakpoint().
		// </summary>
		public void RemoveBreakpoint (int index)
		{
			lock (this) {
				if (!breakpoints.Contains (index))
					return;

				BreakpointHandle handle = (BreakpointHandle) breakpoints [index];
				handle.DisableBreakpoint ();
				breakpoints.Remove (index);
				OnBreakpointsChangedEvent ();
			}
		}

		// <summary>
		//   Returns all breakpoints which have been registered with this module.
		// </summary>
		public Breakpoint[] Breakpoints {
			get {
				lock (this) {
					ArrayList list = new ArrayList ();
					foreach (BreakpointHandle handle in breakpoints.Values)
						list.Add (handle.Breakpoint);
					Breakpoint[] retval = new Breakpoint [list.Count];
					list.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		// <summary>
		//   Returns all breakpoints which have been registered with this module.
		// </summary>
		public BreakpointHandle[] BreakpointHandles {
			get {
				lock (this) {
					ArrayList list = new ArrayList ();
					foreach (BreakpointHandle handle in breakpoints.Values)
						list.Add (handle);
					BreakpointHandle[] retval = new BreakpointHandle [list.Count];
					list.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		// <remarks>
		//   This is called from the SymbolTableManager's background thread when
		//   the module is changed.  It creates a hash table which maps a method
		//   name to a SourceMethod and a list of SourceMethod's which is
		//   sorted by the method's start line.
		// </remarks>
		protected internal void ReadModuleData ()
		{
			lock (this) {
				if (module_data == null)
					return;

				module_data.ReadModuleData ();
			}
		}

		// <summary>
		//   Returns a list of all source files in this method.
		//   May only be used while @SymbolsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if @SymbolsLoaded was false.
		// </summary>
		public SourceFile[] Sources {
			get {
				lock (this) {
					if ((module_data == null) || !module_data.SymbolsLoaded)
						throw new InvalidOperationException ();

					return module_data.Sources;
				}
			}
		}

		// <summary>
		//   Find method @name, which must be a full method name including the
		//   signature (System.DateTime.GetUtcOffset(System.DateTime)).
		// </summary>
		public virtual SourceMethod FindMethod (string name)
		{
			if (!SymbolsLoaded)
				return null;

			ReadModuleData ();

			return module_data.FindMethod (name);
		}

		// <summary>
		//   Find the method containing line @line in @source_file, which must be
		//   the file's full pathname.
		// </summary>
		public virtual SourceLocation FindLocation (string source_file, int line)
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

		public TargetAddress SimpleLookup (string name)
		{
			lock (this) {
				if (module_data == null)
					return TargetAddress.Null;

				return module_data.SimpleLookup (name);
			}
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
				lock (this) {
					if ((module_data == null) || !module_data.SymbolsLoaded)
						throw new InvalidOperationException ();

					return module_data.SymbolTable;
				}
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})",
					      GetType (), Name, IsLoaded, SymbolsLoaded, StepInto,
					      LoadSymbols);
		}

		//
		// IDeserializationCallback
		//

		bool initialized = false;
		ArrayList create_bpts = null;

		public void OnDeserialization (object sender)
		{
			if (initialized)
				throw new InternalError ();
			initialized = true;

			foreach (BreakpointHandle handle in create_bpts)
				AddBreakpoint (handle);

			create_bpts = null;
		}

		//
		// ISerializable
		//

		public virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("name", Name);
			info.AddValue ("load_symbols", LoadSymbols);
			info.AddValue ("step_into", StepInto);

			ArrayList list = new ArrayList ();
			foreach (BreakpointHandle handle in breakpoints.Values)
				list.Add (handle);
			info.AddValue ("breakpoints", list);
		}

		protected Module (SerializationInfo info, StreamingContext context)
		{
			name = info.GetString ("name");
			load_symbols = info.GetBoolean ("load_symbols");
			step_into = info.GetBoolean ("step_into");

			create_bpts = (ArrayList) info.GetValue ("breakpoints", typeof (ArrayList));
		}
	}
}
