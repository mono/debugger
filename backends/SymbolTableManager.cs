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
		ModuleManager module_manager;
		bool symtab_thread_exit;

		ST.Thread symtab_thread;
		ST.AutoResetEvent symtab_reload_event;
		ST.ManualResetEvent update_completed_event;
		bool symtab_update_in_progress;

		internal SymbolTableManager (ModuleManager module_manager)
		{
			this.module_manager = module_manager;

			module_manager.ModulesChanged += modules_changed;

			symtab_reload_event = new ST.AutoResetEvent (false);
			update_completed_event = new ST.ManualResetEvent (true);
			symtab_thread = new ST.Thread (new ST.ThreadStart (symtab_thread_start));
			symtab_thread.IsBackground = true;
			symtab_thread.Start ();
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

		public void Wait ()
		{
			if (symtab_thread != null) {
				update_completed_event.WaitOne ();
			}
		}

		public Method Lookup (TargetAddress address)
		{
			if (symtab_thread != null)
				update_completed_event.WaitOne ();

			lock (this) {
				if (current_symtab == null)
					return null;
				return current_symtab.Lookup (address);
			}
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (symtab_thread != null)
				update_completed_event.WaitOne ();

			Module[] current_modules = module_manager.Modules;
			foreach (Module module in current_modules) {
				Symbol name = module.SimpleLookup (address, exact_match);
				if (name != null)
					return name;
			}

			return null;
		}

		void modules_changed ()
		{
			lock (this) {
				symtab_reload_event.Set ();
				update_completed_event.Reset ();
				symtab_update_in_progress = true;
			}
		}

		//
		// The following fields are shared between the two threads !
		//
		ISymbolTable current_symtab = null;

		// <summary>
		//   This thread reloads the symbol tables in the background.
		// </summary>

		void symtab_thread_start ()
		{
			Report.Debug (DebugFlags.Threads, "Symtab thread started: {0}",
				      DebuggerWaitHandle.CurrentThread);

			symtab_thread_main ();

			update_completed_event.Set ();
			symtab_update_in_progress = false;
			symtab_thread = null;
		}

		void symtab_thread_main ()
		{
			while (true) {
				symtab_reload_event.WaitOne ();

				if (symtab_thread_exit)
					return;

				// Updating the symbol tables doesn't take that long and they're also
				// needed by the SingleSteppingEngine, so let's do this first.

				SymbolTableCollection symtabs = new SymbolTableCollection ();
				symtabs.Lock ();

				Module[] current_modules = module_manager.Modules;
				foreach (Module module in current_modules) {
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
					symtab_update_in_progress = false;
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

						module_manager.ModulesChanged -= modules_changed;
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
