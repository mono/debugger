using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	public delegate void ModulesChangedHandler ();
	public delegate void BreakpointsChangedHandler ();

	internal class ModuleManager : DebuggerMarshalByRefObject
	{
		DebuggerServant backend;
		Hashtable modules;

		internal ModuleManager (DebuggerServant backend)
		{
			this.backend = backend;
			modules = Hashtable.Synchronized (new Hashtable ());
		}

		public Module GetModule (string name)
		{
			return (Module) modules [name];
		}

		internal Module CreateModule (string name, ModuleGroup group)
		{
			Module module = (Module) modules [name];
			if (module != null)
				return module;

			module = new Module (this, group, name, null);
			modules.Add (name, module);

			new ModuleEventSink (this, module);
			module_changed (module);

			return module;
		}

		internal Module CreateModule (string name, SymbolFile symfile)
		{
			if (symfile == null)
				throw new NullReferenceException ();

			Module module = (Module) modules [name];
			if (module != null)
				return module;

			ModuleGroup group = backend.Configuration.GetModuleGroup (symfile);

			module = new Module (this, group, name, symfile);
			modules.Add (name, module);

			new ModuleEventSink (this, module);
			module_changed (module);

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
