using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public delegate void ModuleEventHandler (Module module);

	public abstract class Module : ISerializable
	{
		string name;
		bool load_symbols;
		bool step_into;
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

		public string Name {
			get {
				return name;
			}
		}

		public abstract string FullName {
			get;
		}

		public abstract bool IsLoaded {
			get;
		}

		public abstract bool SymbolsLoaded {
			get;
		}

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

		protected abstract void SymbolsChanged (bool loaded);

		public virtual void UnLoad ()
		{ }

		public event ModuleEventHandler ModuleLoadedEvent;
		public event ModuleEventHandler ModuleUnLoadedEvent;
		public event ModuleEventHandler SymbolsLoadedEvent;
		public event ModuleEventHandler SymbolsUnLoadedEvent;
		public event ModuleEventHandler ModuleChangedEvent;
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
			Console.WriteLine ("ON BREAKPOINTS CHANGED");
			if (BreakpointsChangedEvent != null)
				BreakpointsChangedEvent (this);
		}

		protected abstract void AddBreakpoint (BreakpointHandle handle);

		protected abstract void RemoveBreakpoint (BreakpointHandle handle);

		protected abstract object EnableBreakpoint (BreakpointHandle handle, TargetAddress address);

		protected abstract void DisableBreakpoint (BreakpointHandle handle, object data);

		public int AddBreakpoint (Breakpoint breakpoint, SourceMethodInfo method)
		{
			return AddBreakpoint (breakpoint, method, 0);
		}

		public int AddBreakpoint (Breakpoint breakpoint, SourceMethodInfo method, int line)
		{
			int index = ++next_breakpoint_id;
			BreakpointHandle handle = new BreakpointHandle (
				this, breakpoint, method, line, index);
			breakpoints.Add (index, handle);
			OnBreakpointsChangedEvent ();
			return index;
		}

		public void RemoveBreakpoint (int index)
		{
			if (!breakpoints.Contains (index))
				return;

			BreakpointHandle handle = (BreakpointHandle) breakpoints [index];
			handle.Dispose ();
			breakpoints.Remove (index);
			OnBreakpointsChangedEvent ();
		}

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

		protected internal virtual void ReadModuleData ()
		{
			lock (this) {
				if (!LoadSymbols)
					return;

				if (sources != null)
					return;

				sources = GetSources ();
				if (sources == null)
					sources = new SourceInfo [0];
			}
		}

		protected abstract SourceInfo[] GetSources ();

		public SourceInfo[] Sources {
			get {
				return sources;
			}
		}

		public virtual SourceMethodInfo FindMethod (string name)
		{
			if (!SymbolsLoaded)
				return null;

			foreach (SourceInfo source in Sources) {
				SourceMethodInfo method = source.FindMethod (name);

				if (method != null)
					return method;
			}

			return null;
		}

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

			void module_unloaded (Module module)
			{
				Method = null;
			}

			void module_loaded (Module module)
			{
				Method = Module.FindMethod (MethodName);
			}

			void method_loaded (SourceMethodInfo method, object user_data)
			{
				load_handler = null;

				TargetAddress address;
				if (Line != 0)
					address = Method.Lookup (Line);
				else
					address = Method.Method.StartAddress;

				Console.WriteLine ("METHOD LOADED: {0} {1}", method, address);

				if (address.IsNull)
					return;

				handle = Module.EnableBreakpoint (this, address);
				enabled = handle != null;
			}

			public void Enable ()
			{
				if (enabled || !Module.IsLoaded || !Breakpoint.Enabled) {
					Module.OnBreakpointsChangedEvent ();
					return;
				}

				if (Method.IsLoaded) {
					handle = Module.EnableBreakpoint (this, Method.Method.StartAddress);
					if (handle != null)
						enabled = true;
				} else if (Method.IsDynamic) {
					load_handler = Method.RegisterLoadHandler (
						new MethodLoadedHandler (method_loaded), null);
					if (load_handler != null)
						enabled = true;
				}

				Module.OnBreakpointsChangedEvent ();
			}

			public void Disable ()
			{
				if (!enabled) {
					Module.OnBreakpointsChangedEvent ();
					return;
				}

				if (load_handler != null) {
					load_handler.Dispose ();
					load_handler = null;
				} else {
					Module.DisableBreakpoint (this, handle);
					handle = null;
				}

				enabled = false;
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
