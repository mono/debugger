using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public class BreakpointHandle
	{
		Process process;
		Module module;
		ThreadGroup group;
		Breakpoint breakpoint;
		SourceLocation location;
		bool is_loaded;

		public BreakpointHandle (Process process, Breakpoint breakpoint, Module module,
					 ThreadGroup group, SourceLocation location)
		{
			this.process = process;
			this.module = module;
			this.group = group;
			this.breakpoint = breakpoint;
			this.location = location;

			initialize ();
		}

		public Module Module {
			get { return module; }
		}

		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public ThreadGroup ThreadGroup {
			get { return group; }
		}

		void initialize ()
		{
			module.SymbolsLoadedEvent += new ModuleEventHandler (SymbolsLoaded);
			module.ModuleLoadedEvent += new ModuleEventHandler (ModuleLoaded);
			module.ModuleUnLoadedEvent += new ModuleEventHandler (ModuleUnLoaded);

			if (module.IsLoaded)
				ModuleLoaded (module);
		}

		public bool Breaks (int id)
		{
			if (group == null)
				return true;

			foreach (int thread in group.Threads) {
				if (thread == id)
					return true;
			}

			return false;
		}

		IDisposable load_handler;

		// <summary>
		//   The method has just been loaded, lookup the breakpoint
		//   address and actually insert it.
		// </summary>
		void method_loaded (SourceMethod method, object user_data)
		{
			load_handler = null;

			bpt_address = location.GetAddress ();
			if (bpt_address.IsNull)
				return;

			EnableBreakpoint (process);
		}

		protected virtual void SymbolsLoaded (Module module)
		{
			if (module.IsLoaded)
				ModuleLoaded (module);
		}

		protected virtual void ModuleLoaded (Module module)
		{
			if (is_loaded)
				return;
			is_loaded = true;
			if (location.Method.IsLoaded) {
				bpt_address = location.GetAddress ();
				EnableBreakpoint (process);
			} else if (location.Method.IsDynamic) {
				// A dynamic method is a method which may emit a
				// callback when it's loaded.  We register this
				// callback here and do the actual insertion when
				// the method is loaded.
				load_handler = location.Method.RegisterLoadHandler (
					new MethodLoadedHandler (method_loaded), null);
			}
		}

		protected virtual void ModuleUnLoaded (Module module)
		{
			is_loaded = false;
			if (load_handler != null) {
				load_handler.Dispose ();
				load_handler = null;
			}
			DisableBreakpoint (process);
		}

		TargetAddress bpt_address = TargetAddress.Null;
		object breakpoint_data = null;

		public bool IsEnabled {
			get { return breakpoint_data != null; }
		}

		protected void Enable (Process process)
		{
			lock (this) {
				if ((bpt_address.IsNull) || (breakpoint_data != null))
					return;

				ModuleData module_data = module.ModuleData;
				if (module_data == null)
					throw new InternalError ();

				breakpoint_data = module_data.EnableBreakpoint (
					process, this, bpt_address);
			}
		}

		protected void Disable (Process process)
		{
			lock (this) {
				ModuleData module_data = module.ModuleData;
				if ((module_data != null) && (breakpoint_data != null))
					module_data.DisableBreakpoint (
						process, this, breakpoint_data);
				breakpoint_data = null;
			}
		}

		public void EnableBreakpoint (Process process)
		{
			lock (this) {
				Enable (process);
			}
		}

		public void DisableBreakpoint (Process process)
		{
			lock (this) {
				Disable (process);
			}
		}

		public void RemoveBreakpoint (Process process)
		{
			is_loaded = false;
			if (load_handler != null) {
				load_handler.Dispose ();
				load_handler = null;
			}
			DisableBreakpoint (process);
		}
	}
}
