using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	[Serializable]
	public class BreakpointHandle : ISerializable, IDeserializationCallback
	{
		Module module;
		ThreadGroup group;
		Breakpoint breakpoint;
		SourceLocation location;
		bool is_loaded;

		public BreakpointHandle (Breakpoint breakpoint, Module module, ThreadGroup group,
					 SourceLocation location)
		{
			this.module = module;
			this.group = group;
			this.breakpoint = breakpoint;
			this.location = location;

			Initialize ();
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

		void Initialize ()
		{
			if (initialized)
				throw new InternalError ();
			initialized = true;

			module.SymbolsLoadedEvent += new ModuleEventHandler (SymbolsLoaded);
			module.ModuleLoadedEvent += new ModuleEventHandler (ModuleLoaded);
			module.ModuleUnLoadedEvent += new ModuleEventHandler (ModuleUnLoaded);

			breakpoint.BreakpointChangedEvent += new BreakpointEventHandler (breakpoint_changed);

			if (module.IsLoaded)
				ModuleLoaded (module);
		}

		public bool Breaks (Process process)
		{
			if (group == null)
				return true;

			int id = process.ID;

			foreach (Process thread in group.Threads) {
				if (thread.ID == id)
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

			TargetAddress address = location.GetAddress ();
			if (address.IsNull)
				return;

			EnableBreakpoint (address);
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
			if (location.Method.IsLoaded)
				EnableBreakpoint (location.GetAddress ());
			else if (location.Method.IsDynamic) {
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
			DisableBreakpoint ();
		}

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
			module.OnBreakpointsChangedEvent ();
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

				breakpoint_data = module_data.EnableBreakpoint (this, bpt_address);
			}
		}

		protected void Disable ()
		{
			lock (this) {
				ModuleData module_data = module.ModuleData;
				if ((module_data != null) && (breakpoint_data != null))
					module_data.DisableBreakpoint (this, breakpoint_data);
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

		//
		// IDeserializationCallback
		//

		bool initialized = false;

		public void OnDeserialization (object sender)
		{
			Initialize ();
		}

		//
		// ISerializable
		//

		public virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("breakpoint", breakpoint);
			info.AddValue ("module", module);
			info.AddValue ("group", group);
			info.AddValue ("location", location);
		}

		protected BreakpointHandle (SerializationInfo info, StreamingContext context)
		{
			breakpoint = (Breakpoint) info.GetValue ("breakpoint", typeof (Breakpoint));
			module = (Module) info.GetValue ("module", typeof (Module));
			group = (ThreadGroup) info.GetValue ("group", typeof (ThreadGroup));
			location = (SourceLocation) info.GetValue ("location", typeof (SourceLocation));
		}
	}
}
