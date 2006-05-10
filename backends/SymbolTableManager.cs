using System;
using System.Collections;
using ST = System.Threading;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Backends
{
	// <summary>
	//   This class maintains the debugger's symbol tables.
	// </summary>
	internal class SymbolTableManager : DebuggerMarshalByRefObject, IDisposable
	{
		bool symtab_thread_exit;

		ST.Thread symtab_thread;
		ST.AutoResetEvent symtab_reload_event;
		ST.ManualResetEvent symtabs_loaded_event;
		ST.ManualResetEvent modules_loaded_event;
		ST.ManualResetEvent update_completed_event;
		bool symtab_update_in_progress;
		bool module_update_in_progress;

		internal SymbolTableManager ()
		{
			symtab_reload_event = new ST.AutoResetEvent (false);
			symtabs_loaded_event = new ST.ManualResetEvent (true);
			modules_loaded_event = new ST.ManualResetEvent (true);
			update_completed_event = new ST.ManualResetEvent (true);
			symtab_thread = new ST.Thread (new ST.ThreadStart (symtab_thread_start));
			symtab_thread.IsBackground = true;
			symtab_thread.Start ();
		}

		// <summary>
		//   Tell the SymbolTableManager that the modules have changed.  It will reload
		//   the symbol tables in the background and emit the SymbolTableChangedEvent
		//   when done.
		// </summary>
		internal void SetModules (ICollection modules)
		{
			lock (this) {
				new_modules = modules;
				symtab_reload_event.Set ();
				symtabs_loaded_event.Reset ();
				modules_loaded_event.Reset ();
				update_completed_event.Reset ();
				symtab_update_in_progress = true;
				module_update_in_progress = true;
			}
		}

		// <summary>
		//   Whether an update is currently in progress.
		// </summary>
		internal bool SymbolTableUpdateInProgress {
			get {
				lock (this) {
					return symtab_update_in_progress;
				}
			}
		}

		// <summary>
		//   Whether an update is currently in progress.
		// </summary>
		internal bool ModuleUpdateInProgress {
			get {
				lock (this) {
					return module_update_in_progress;
				}
			}
		}

		public void Wait ()
		{
			if (symtab_thread != null) {
				update_completed_event.WaitOne ();
			}
		}

		internal delegate void ModuleHandler (object sender, Module[] modules);

		// <summary>
		//   This event is emitted each time the modules have changed.
		//   The modules won't change while this handler is running.
		// </summary>
		internal event ModuleHandler ModulesChangedEvent;

		// <summary>
		//   The current modules.  This property may change at any time, so you
		//   should use the ModulesChangedEvent to get a notification.
		// </summary>
		public Module[] Modules {
			get {
				if (symtab_thread != null)
					modules_loaded_event.WaitOne ();
				lock (this) {
					return current_modules;
				}
			}
		}

		public Method Lookup (TargetAddress address)
		{
			if (symtab_thread != null)
				symtabs_loaded_event.WaitOne ();
			lock (this) {
				if (current_symtab == null)
					return null;
				return current_symtab.Lookup (address);
			}
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (symtab_thread != null) {
				symtabs_loaded_event.WaitOne ();
				modules_loaded_event.WaitOne ();
			}

			lock (this) {
				if (current_modules == null)
					return null;

				foreach (Module module in current_modules) {
					Symbol name = module.SimpleLookup (address, exact_match);
					if (name != null)
						return name;
				}

				return null;
			}
		}

		protected virtual void OnModulesChanged ()
		{
			lock (this) {
				if (ModulesChangedEvent != null)
					ModulesChangedEvent (this, current_modules);
			}
		}

		//
		// The following fields are shared between the two threads !
		//
		ICollection new_modules = null;
		ISymbolTable current_symtab = null;
		Module[] current_modules = null;

		// <summary>
		//   This thread reloads the symbol tables in the background.
		// </summary>

		void symtab_thread_start ()
		{
			Report.Debug (DebugFlags.Threads, "Symtab thread started: {0}",
				      DebuggerWaitHandle.CurrentThread);

			symtab_thread_main ();

			symtabs_loaded_event.Set ();
			modules_loaded_event.Set ();
			update_completed_event.Set ();
			symtab_update_in_progress = false;
			module_update_in_progress = false;
			symtab_thread = null;
		}

		void symtab_thread_main ()
		{
			while (true) {
				symtab_reload_event.WaitOne ();

				if (symtab_thread_exit)
					return;

				ICollection my_new_modules;

				lock (this) {
					my_new_modules = new_modules;
					new_modules = null;
				}

				if (my_new_modules == null) {
					// Nothing to do, clear the events and continue.
					symtabs_loaded_event.Set ();
					modules_loaded_event.Set ();
					update_completed_event.Set ();
					symtab_update_in_progress = false;
					module_update_in_progress = false;
					continue;
				}

				// Updating the symbol tables doesn't take that long and they're also
				// needed by the SingleSteppingEngine, so let's do this first.

				SymbolTableCollection symtabs = new SymbolTableCollection ();
				symtabs.Lock ();

				foreach (Module module in my_new_modules) {
					if (!module.SymbolsLoaded || !module.LoadSymbols)
						continue;

					ISymbolTable symtab = module.SymbolTable;
					symtabs.AddSymbolTable (symtab);
				}

				symtabs.UnLock ();

				lock (this) {
					current_symtab = symtabs;
					// We need to clear this event as soon as we're done updating
					// the symbol tables since the main thread may be waiting in
					// the `SymbolTable' accessor.
					symtabs_loaded_event.Set ();
					symtab_update_in_progress = false;
				}

				lock (this) {
					current_modules = new Module [my_new_modules.Count];
					my_new_modules.CopyTo (current_modules, 0);
					modules_loaded_event.Set ();
					module_update_in_progress = false;
					OnModulesChanged ();
					update_completed_event.Set ();
				}
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("SymbolTableManager");
		}

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					if (symtab_thread != null) {
						symtab_thread_exit = true;
						symtab_reload_event.Set ();
						symtab_thread = null;
					}
				}
				
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~SymbolTableManager ()
		{
			Dispose (false);
		}
	}
}
