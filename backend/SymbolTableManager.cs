using System;
using System.Collections;
using ST = System.Threading;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Backend
{
	// <summary>
	//   This class maintains the debugger's symbol tables.
	// </summary>
	internal class SymbolTableManager : DebuggerMarshalByRefObject, ISymbolTable, IDisposable
	{
		ArrayList symbol_files;

		internal SymbolTableManager (DebuggerSession session)
		{
			this.symbol_files = ArrayList.Synchronized (new ArrayList ());
		}

		internal void AddSymbolFile (SymbolFile symfile)
		{
			symbol_files.Add (symfile);
		}

		//
		// ISymbolLookup
		//

		public Method Lookup (TargetAddress address)
		{
			foreach (SymbolFile symfile in symbol_files) {
				if (!symfile.SymbolsLoaded)
					continue;

				Method method = symfile.SymbolTable.Lookup (address);
				if (method != null)
					return method;
			}

			return null;
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			foreach (SymbolFile symfile in symbol_files) {
				Symbol name = symfile.SimpleLookup (address, exact_match);
				if (name != null)
					return name;
			}

			return null;
		}

		//
		// ISymbolContainer
		//

		bool ISymbolContainer.IsContinuous {
			get { return false; }
		}

		TargetAddress ISymbolContainer.StartAddress {
			get { throw new InvalidOperationException (); }
		}

		TargetAddress ISymbolContainer.EndAddress {
			get { throw new InvalidOperationException (); }
		}

		//
		// ISymbolTable
		//

		bool ISymbolTable.HasRanges {
			get { return false; }
		}

		ISymbolRange[] ISymbolTable.SymbolRanges {
			get { throw new InvalidOperationException (); }
		}

		bool ISymbolTable.HasMethods {
			get { return false; }
		}

		Method[] ISymbolTable.Methods {
			get { throw new InvalidOperationException (); }
		}

		bool ISymbolTable.IsLoaded {
			get { return true; }
		}

		void ISymbolTable.UpdateSymbolTable ()
		{ }

		public event SymbolTableChangedHandler SymbolTableChanged;

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
					symbol_files = ArrayList.Synchronized (new ArrayList ());
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
