using System;

namespace Mono.Debugger
{
	public abstract class BreakpointHandle
	{
		Module module;
		ThreadGroup group;
		Breakpoint breakpoint;

		protected BreakpointHandle (Breakpoint breakpoint, Module module, ThreadGroup group)
		{
			this.module = module;
			this.group = group;
			this.breakpoint = breakpoint;

			module.ModuleLoadedEvent += new ModuleEventHandler (ModuleLoaded);
			module.ModuleUnLoadedEvent += new ModuleEventHandler (ModuleUnLoaded);

			breakpoint.BreakpointChangedEvent += new BreakpointEventHandler (breakpoint_changed);
		}

		public Module Module {
			get { return module; }
		}

		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		protected abstract void ModuleLoaded (Module module);
		protected abstract void ModuleUnLoaded (Module module);

		// <summary>
		//   This is called via the Breakpoint.BreakpointChangedEvent to
		//   actually enable/disable the breakpoint.
		// </summary>
		void breakpoint_changed (Breakpoint breakpoint)
		{
			if (breakpoint.Enabled)
				Enable ();
			else
				Disable ();
		}

		TargetAddress bpt_address = TargetAddress.Null;
		object breakpoint_data = null;

		public bool IsEnabled {
			get { return breakpoint_data != null; }
		}

		protected void Enable ()
		{
			lock (this) {
				if ((bpt_address.IsNull) || (breakpoint_data != null))
					return;

				ModuleData module_data = module.ModuleData;
				if (module_data == null)
					throw new InternalError ();

				breakpoint_data = module_data.EnableBreakpoint (this, group, bpt_address);
			}
		}

		protected void Disable ()
		{
			lock (this) {
				ModuleData module_data = module.ModuleData;
				if ((module_data != null) && (breakpoint_data != null))
					module_data.DisableBreakpoint (this, group, breakpoint_data);
				breakpoint_data = null;
			}
		}

		protected void EnableBreakpoint (TargetAddress address)
		{
			lock (this) {
				bpt_address = address;
				if (breakpoint.Enabled)
					Enable ();
			}
		}

		protected internal void DisableBreakpoint ()
		{
			lock (this) {
				Disable ();
				bpt_address = TargetAddress.Null;
			}
		}
	}

	public class BreakpointHandleMethod : BreakpointHandle
	{
		SourceMethodInfo method;
		int line;

		public BreakpointHandleMethod (Breakpoint breakpoint, Module module, ThreadGroup group,
					       SourceMethodInfo method)
			: this (breakpoint, module, group, method, -1)
		{ }

		public BreakpointHandleMethod (Breakpoint breakpoint, Module module, ThreadGroup group,
					       SourceMethodInfo method, int line)
			: base (breakpoint, module, group)
		{
			this.method = method;
			this.line = line;

			if (module.IsLoaded)
				ModuleLoaded (module);
		}

		protected TargetAddress get_address (SourceMethodInfo method)
		{
			if (line != -1)
				return method.Lookup (line);
			else if (method.Method.HasMethodBounds)
				return method.Method.MethodStartAddress;
			else
				return method.Method.StartAddress;
		}

		IDisposable load_handler;

		// <summary>
		//   The method has just been loaded, lookup the breakpoint
		//   address and actually insert it.
		// </summary>
		void method_loaded (SourceMethodInfo method, object user_data)
		{
			load_handler = null;

			TargetAddress address = get_address (method);
			if (address.IsNull)
				return;

			EnableBreakpoint (address);
		}

		protected override void ModuleLoaded (Module module)
		{
			if (method.IsLoaded)
				EnableBreakpoint (get_address (method));
			else if (method.IsDynamic) {
				// A dynamic method is a method which may emit a
				// callback when it's loaded.  We register this
				// callback here and do the actual insertion when
				// the method is loaded.
				load_handler = method.RegisterLoadHandler (
					new MethodLoadedHandler (method_loaded), null);
			}
		}

		protected override void ModuleUnLoaded (Module module)
		{
			if (load_handler != null) {
				load_handler.Dispose ();
				load_handler = null;
			}
			DisableBreakpoint ();
		}
	}
}
