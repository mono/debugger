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
		ManualResetEvent symtab_reload_event;
		ManualResetEvent symtabs_loaded_event;
		ManualResetEvent modules_loaded_event;
		bool symtab_update_in_progress;
		bool module_update_in_progress;
		bool reload_requested;
		AsyncQueue async_queue;

		public SymbolTableManager ()
		{
			async_queue = new AsyncQueue (new AsyncQueueHandler (async_handler));
			symtab_reload_event = new ManualResetEvent (false);
			symtabs_loaded_event = new ManualResetEvent (true);
			modules_loaded_event = new ManualResetEvent (true);
			symtab_thread = new Thread (new ThreadStart (symtab_thread_start));
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
				symtab_update_in_progress = true;
				module_update_in_progress = true;
				reload_requested = true;
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

		public delegate void SymbolTableHandler (object sender, ISymbolTable symbol_table);
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
					symtabs_loaded_event.WaitOne ();
				lock (this) {
					return current_symtab;
				}
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
					modules_loaded_event.WaitOne ();
				lock (this) {
					return current_modules;
				}
			}
		}

		protected virtual void OnSymbolTableChanged ()
		{
			lock (this) {
				if (SymbolTableChangedEvent != null)
					SymbolTableChangedEvent (this, current_symtab);
			}
		}

		protected virtual void OnModulesChanged ()
		{
			lock (this) {
				if (ModulesChangedEvent != null)
					ModulesChangedEvent (this, current_modules);
			}
		}

		void async_handler (string message)
		{
			if (message == "M")
				OnModulesChanged ();
			else
				OnSymbolTableChanged ();
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
			try {
				symtab_thread_main ();
			} catch (ThreadAbortException) {
				symtabs_loaded_event.Set ();
				modules_loaded_event.Set ();
				symtab_update_in_progress = false;
				module_update_in_progress = false;
				symtab_thread = null;
			}
		}

		void symtab_thread_main ()
		{
			while (symtab_reload_event.WaitOne ()) {
			again:
				ICollection my_new_modules;

				lock (this) {
					my_new_modules = new_modules;
					new_modules = null;
					// We must clear the event and the flag while we're still holding
					// the lock to avoid a race condition.
					symtab_reload_event.Reset ();
					reload_requested = false;
				}

				if (my_new_modules == null) {
					// Nothing to do, clear the events and continue.
					symtabs_loaded_event.Set ();
					modules_loaded_event.Set ();
					symtab_update_in_progress = false;
					module_update_in_progress = false;
					goto again;
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
					// After acquiring the lock, check whether another reload was
					// requested in the meantime.  If so, discard the symtabs we
					// just created and do another update.
					if (reload_requested)
						goto again;

					current_symtab = symtabs;
					// We need to clear this event as soon as we're done updating
					// the symbol tables since the main thread may be waiting in
					// the `SymbolTable' accessor.
					symtabs_loaded_event.Set ();
					symtab_update_in_progress = false;
					async_queue.Write ("S");
				}

				// Ok, we're now done updating the symbol tables so we can update the
				// module list.  This is a more CPU consuming operation so we're doing
				// this last.

				foreach (Module module in my_new_modules) {
					module.ReadModuleData ();

					// Reading the module data may be a very CPU consuming operation,
					// so after reading each module, check whether another reload has
					// been requested in the meantime.
					if (reload_requested)
						goto again;
				}

				lock (this) {
					current_modules = new Module [my_new_modules.Count];
					my_new_modules.CopyTo (current_modules, 0);
					modules_loaded_event.Set ();
					module_update_in_progress = false;
					async_queue.Write ("M");
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
						async_queue.Dispose ();
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
