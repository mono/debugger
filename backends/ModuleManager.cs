using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	public delegate void ModulesChangedHandler ();
	public delegate void BreakpointsChangedHandler ();

	internal class ModuleManager : MarshalByRefObject
	{
		Hashtable modules = new Hashtable ();

		internal ModuleManager ()
		{
		}

		public Module GetModule (string name)
		{
			return (Module) modules [name];
		}

		public void AddModule (Module module)
		{
			modules.Add (module.Name, module);

			new ModuleEventSink (this, module);

			module_changed (module);
		}

		internal Module CreateModule (string name)
		{
			Module module = (Module) modules [name];
			if (module != null)
				return module;

			module = new Module (name);
			modules.Add (name, module);
			return module;
		}

		public event ModulesChangedHandler ModulesChanged;
		public event BreakpointsChangedHandler BreakpointsChanged;

		public Module[] Modules {
			get {
				Module[] retval = new Module [modules.Values.Count];
				modules.Values.CopyTo (retval, 0);
				return retval;
			}
		}

		int locked = 0;
		bool needs_module_update = false;
		bool needs_breakpoint_update = false;

		public void Lock ()
		{
			locked++;
		}

		public void UnLock ()
		{
			if (--locked > 0)
				return;

			if (needs_module_update) {
				needs_module_update = false;
				OnModulesChanged ();
			}
			if (needs_breakpoint_update) {
				needs_breakpoint_update = false;
				OnBreakpointsChanged ();
			}
		}

		void module_changed (Module module)
		{
			OnModulesChanged ();
		}

		void breakpoints_changed (Module module)
		{
			OnBreakpointsChanged ();
		}

		protected virtual void OnModulesChanged ()
		{
			if (locked > 0) {
				needs_module_update = true;
				return;
			}

			if (ModulesChanged != null)
				ModulesChanged ();
		}

		protected virtual void OnBreakpointsChanged ()
		{
			if (locked > 0) {
				needs_breakpoint_update = true;
				return;
			}

			if (BreakpointsChanged != null)
				BreakpointsChanged ();
		}

		[Serializable]
		protected class ModuleEventSink
		{
			public readonly ModuleManager Manager;

			public ModuleEventSink (ModuleManager manager, Module module)
			{
				this.Manager = manager;

				module.SymbolsLoadedEvent += OnModuleChanged;
				module.SymbolsUnLoadedEvent += OnModuleChanged;
				module.BreakpointsChangedEvent += OnBreakpointsChanged;
			}

			public void OnModuleChanged (Module module)
			{
				Manager.module_changed (module);
			}

			public void OnBreakpointsChanged (Module module)
			{
				Manager.breakpoints_changed (module);
			}
		}
	}
}
