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

	[Serializable]
	public class ModuleManager : ISerializable, IDeserializationCallback
	{
		Hashtable modules = new Hashtable ();

		public ModuleManager ()
		{
			initialized = true;
		}

		public void AddModule (Module module)
		{
			modules.Add (module.Name, module);

			module.SymbolsLoadedEvent += new ModuleEventHandler (module_changed);
			module.SymbolsUnLoadedEvent += new ModuleEventHandler (module_changed);
			module.BreakpointsChangedEvent += new ModuleEventHandler (breakpoints_changed);

			module_changed (module);
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

		//
		// IDeserializationCallback
		//

		ArrayList deserialized_modules = null;
		bool initialized = false;

		public void OnDeserialization (object sender)
		{
			if (initialized)
				throw new InternalError ();
			initialized = true;

			foreach (Module module in deserialized_modules)
				AddModule (module);

			deserialized_modules = null;
		}

		//
		// ISerializable
		//

		public virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			ArrayList list = new ArrayList ();
			foreach (Module module in modules.Values)
				list.Add (module);
			info.AddValue ("modules", list);
		}

		protected ModuleManager (SerializationInfo info, StreamingContext context)
		{
			deserialized_modules = (ArrayList) info.GetValue ("modules", typeof (ArrayList));
		}
	}
}
