using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public delegate void ModulesChangedHandler ();
	public delegate void BreakpointsChangedHandler ();

	public class ModuleManager
	{
		ArrayList modules = new ArrayList ();

		public void AddModule (Module module)
		{
			modules.Add (module);

			module.ModuleChangedEvent += new ModuleEventHandler (module_changed);
			module.BreakpointsChangedEvent += new ModuleEventHandler (breakpoints_changed);
		}

		public event ModulesChangedHandler ModulesChanged;
		public event BreakpointsChangedHandler BreakpointsChanged;

		public Module[] Modules {
			get {
				Module[] retval = new Module [modules.Count];
				modules.CopyTo (retval, 0);
				return retval;
			}
		}

		bool locked = false;
		bool needs_module_update = false;
		bool needs_breakpoint_update = false;

		public bool Locked {
			get {
				return locked;
			}

			set {
				locked = value;
				if (!locked) {
					if (needs_module_update) {
						needs_module_update = false;
						OnModulesChanged ();
					}
					if (needs_breakpoint_update) {
						needs_breakpoint_update = false;
						OnBreakpointsChanged ();
					}
				}
			}
		}

		public void UnLoadAllModules ()
		{
			Locked = true;
			foreach (Module module in modules)
				module.UnLoad ();
			Locked = false;
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
			if (locked) {
				needs_module_update = true;
				return;
			}

			if (ModulesChanged != null)
				ModulesChanged ();
		}

		protected virtual void OnBreakpointsChanged ()
		{
			if (locked) {
				needs_breakpoint_update = true;
				return;
			}

			if (BreakpointsChanged != null)
				BreakpointsChanged ();
		}
	}
}
