using System;
using System.Collections;
using System.Threading;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	// <summary>
	//   This class maintains the debugger's symbol tables.
	// </summary>
	public class SymbolTableManager : IDisposable
	{
		Thread symtab_thread;
		DebuggerAutoResetEvent symtab_reload_event;
		DebuggerManualResetEvent symtabs_loaded_event;
		DebuggerManualResetEvent modules_loaded_event;
		DebuggerManualResetEvent update_completed_event;
		bool symtab_update_in_progress;
		bool module_update_in_progress;

		public SymbolTableManager ()
		{
			symtab_reload_event = new DebuggerAutoResetEvent (
				"symtab_reload_event", false);
			symtabs_loaded_event = new DebuggerManualResetEvent (
				"symtabs_loaded_event", true);
			modules_loaded_event = new DebuggerManualResetEvent (
				"modules_loaded_event", true);
			update_completed_event = new DebuggerManualResetEvent (
				"update_completed_event", true);
			symtab_thread = new Thread (new ThreadStart (symtab_thread_start));
			symtab_thread.IsBackground = true;
			symtab_thread.Start ();
		}

		// <summary>
		//   Tell the SymbolTableManager that the modules have changed.  It will reload
		//   the symbol tables in the background and emit the SymbolTableChangedEvent
		//   when done.
		// </summary>
		public void SetModules (ICollection modules)
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
		public bool SymbolTableUpdateInProgress {
			get {
				lock (this) {
					return symtab_update_in_progress;
				}
			}
		}

		// <summary>
		//   Whether an update is currently in progress.
		// </summary>
		public bool ModuleUpdateInProgress {
			get {
				lock (this) {
					return module_update_in_progress;
				}
			}
		}

		public delegate void SymbolTableHandler (object sender, ISymbolTable symbol_table,
							 ISimpleSymbolTable simple_symtab);
		public delegate void ModuleHandler (object sender, Module[] modules);

		// <summary>
		//   This event is emitted each time the symbol tables have changed.
		//   The symbol tables won't change while this handler is running.
		// </summary>
		public event SymbolTableHandler SymbolTableChangedEvent;

		// <summary>
		//   The current symbol tables.  This property may change at any time, so
		//   you should use the SymbolTableChangedEvent to get a notification.
		// </summary>
		public ISymbolTable SymbolTable {
			get {
				if (symtab_thread != null)
					symtabs_loaded_event.Wait ();
				lock (this) {
					return current_symtab;
				}
			}
		}

		// <summary>
		//   The current symbol tables.  This property may change at any time, so
		//   you should use the SymbolTableChangedEvent to get a notification.
		// </summary>
		public ISimpleSymbolTable SimpleSymbolTable {
			get {
				if (symtab_thread != null)
					symtabs_loaded_event.Wait ();
				lock (this) {
					return current_simple_symtab;
				}
			}
		}

		public void Wait ()
		{
			if (symtab_thread != null) {
				update_completed_event.Wait ();
			}
		}

		// <summary>
		//   This event is emitted each time the modules have changed.
		//   The modules won't change while this handler is running.
		// </summary>
		public event ModuleHandler ModulesChangedEvent;

		// <summary>
		//   The current modules.  This property may change at any time, so you
		//   should use the ModulesChangedEvent to get a notification.
		// </summary>
		public Module[] Modules {
			get {
				if (symtab_thread != null)
					modules_loaded_event.Wait ();
				lock (this) {
					return current_modules;
				}
			}
		}

		protected virtual void OnSymbolTableChanged ()
		{
			lock (this) {
				if (SymbolTableChangedEvent != null)
					SymbolTableChangedEvent (
						this, current_symtab, current_simple_symtab);
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
		ISimpleSymbolTable current_simple_symtab = null;
		Module[] current_modules = null;

		// <summary>
		//   This thread reloads the symbol tables in the background.
		// </summary>

		void symtab_thread_start ()
		{
			try {
				symtab_thread_main ();
			} catch (ThreadAbortException) {
				symtabs_loaded_event.Set ();
				modules_loaded_event.Set ();
				update_completed_event.Set ();
				symtab_update_in_progress = false;
				module_update_in_progress = false;
				symtab_thread = null;
			}
		}

		void symtab_thread_main ()
		{
			while (true) {
				symtab_reload_event.Wait ();
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

				SimpleSymbolTableCollection simple_syms = new SimpleSymbolTableCollection ();

				foreach (Module module in my_new_modules) {
					if (module.IsLoaded)
						simple_syms.AddSymbolTable (module.SimpleSymbolTable);

					if (!module.SymbolsLoaded || !module.LoadSymbols)
						continue;

					ISymbolTable symtab = module.SymbolTable;
					symtabs.AddSymbolTable (symtab);
				}

				symtabs.UnLock ();

				lock (this) {
					current_symtab = symtabs;
					current_simple_symtab = simple_syms;
					// We need to clear this event as soon as we're done updating
					// the symbol tables since the main thread may be waiting in
					// the `SymbolTable' accessor.
					symtabs_loaded_event.Set ();
					symtab_update_in_progress = false;
					OnSymbolTableChanged ();
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
						symtab_thread.Abort ();
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
