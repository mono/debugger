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
			}
		}

		public bool StepInto {
			get {
				return step_into;
			}

			set {
				step_into = value;
			}
		}

		protected abstract void SymbolsChanged (bool loaded);
		public abstract void UnLoad ();

		public event ModuleEventHandler ModuleLoadedEvent;
		public event ModuleEventHandler ModuleUnLoadedEvent;
		public event ModuleEventHandler SymbolsLoadedEvent;
		public event ModuleEventHandler SymbolsUnLoadedEvent;

		protected virtual void OnModuleLoadedEvent ()
		{
			if (ModuleLoadedEvent != null)
				ModuleLoadedEvent (this);

			foreach (BreakpointHandle handle in breakpoints.Values)
				handle.Enable ();
		}

		protected virtual void OnModuleUnLoadedEvent ()
		{
			foreach (BreakpointHandle handle in breakpoints.Values)
				handle.Disable ();

			if (symbol_data != null)
				symbol_data.Dispose ();
			symbol_data = null;

			if (ModuleUnLoadedEvent != null)
				ModuleUnLoadedEvent (this);
		}

		protected virtual void OnSymbolsLoadedEvent ()
		{
			if (!LoadSymbols)
				return;

			if (symbol_data == null)
				symbol_data = new ObjectCache (new ObjectCacheFunc (get_symbol_data),
							       null, new TimeSpan (0,5,0));

			if (SymbolsLoadedEvent != null)
				SymbolsLoadedEvent (this);
		}

		protected virtual void OnSymbolsUnLoadedEvent ()
		{
			if (symbol_data != null)
				symbol_data.Dispose ();
			symbol_data = null;

			if (SymbolsUnLoadedEvent != null)
				SymbolsUnLoadedEvent (this);
		}

		protected abstract void AddBreakpoint (BreakpointHandle handle);

		protected abstract void RemoveBreakpoint (BreakpointHandle handle);

		protected abstract object EnableBreakpoint (BreakpointHandle handle, TargetAddress address);

		protected abstract void DisableBreakpoint (BreakpointHandle handle, object data);

		public int AddBreakpoint (Breakpoint breakpoint, SourceMethodInfo method)
		{
			int index = ++next_breakpoint_id;
			BreakpointHandle handle = new BreakpointHandle (this, breakpoint, method, index);
			breakpoints.Add (index, handle);
			return index;
		}

		public void RemoveBreakpoint (int index)
		{
			if (!breakpoints.Contains (index))
				return;

			BreakpointHandle handle = (BreakpointHandle) breakpoints [index];
			handle.Dispose ();
			breakpoints.Remove (index);
		}

		public Breakpoint[] Breakpoints {
			get {
				Breakpoint[] retval = new Breakpoint [breakpoints.Values.Count];
				breakpoints.Values.CopyTo (retval, 0);
				return retval;
			}
		}

		ObjectCache symbol_data = null;

		object get_symbol_data (object user_data)
		{
			if (!SymbolsLoaded)
				return null;

			return new ModuleSymbolData (this);
		}

		protected abstract SourceInfo[] GetSources ();

		public SourceInfo[] Sources {
			get {
				if (symbol_data == null)
					return new SourceInfo [0];

				return ((ModuleSymbolData) symbol_data.Data).Sources;
			}
		}

		public SourceMethodInfo FindMethod (string name)
		{
			foreach (SourceInfo source in Sources) {
				SourceMethodInfo method = source.FindMethod (name);

				if (method != null)
					return method;
			}

			return null;
		}

		protected struct ModuleSymbolData
		{
			public readonly Module Module;
			public readonly SourceInfo[] Sources;

			public ModuleSymbolData (Module module)
			{
				this.Module = module;

				Sources = null;
				if (Module.SymbolsLoaded)
					Sources = Module.GetSources ();

				if (Sources == null)
					Sources = new SourceInfo [0];
			}
		}

		protected sealed class BreakpointHandle : IDisposable {
			public readonly int Index;
			public readonly Module Module;
			public readonly Breakpoint Breakpoint;
			public readonly string MethodName;
			public SourceMethodInfo Method;

			public BreakpointHandle (Module module, Breakpoint breakpoint,
						 SourceMethodInfo method, int index)
			{
				this.Module = module;
				this.Breakpoint = breakpoint;
				this.Index = index;
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
				handle = Module.EnableBreakpoint (this, Method.Method.StartAddress);
				enabled = handle != null;
			}

			public void Enable ()
			{
				if (enabled)
					return;

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
			}

			public void Disable ()
			{
				if (enabled) {
					enabled = false;

					if (load_handler != null) {
						load_handler.Dispose ();
						load_handler = null;
					} else {
						Module.DisableBreakpoint (this, handle);
						handle = null;
					}
				}
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
