using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class SymbolTableCollection : ISymbolTable, ICollection
	{
		ArrayList symtabs = new ArrayList ();
		ArrayList ranges = new ArrayList ();
		ArrayList symbols = new ArrayList ();
		bool has_symbols;
		bool has_ranges;
		bool in_update;

		public void AddSymbolTable (ISymbolTable symtab)
		{
			if (symtab == null)
				return;
			symtabs.Add (symtab);
			symtab.SymbolTableChanged += new SymbolTableChangedHandler (update_handler);
			update_handler ();
		}

		public bool IsContinuous {
			get {
				return false;
			}
		}

		public TargetAddress StartAddress {
			get {
				throw new InvalidOperationException ();
			}
		}

		public TargetAddress EndAddress {
			get {
				throw new InvalidOperationException ();
			}
		}

		void update_ranges ()
		{
			ranges = new ArrayList ();
			foreach (ISymbolTable symtab in symtabs) {
				if (!symtab.IsLoaded || !symtab.HasRanges)
					continue;

				ranges.AddRange (symtab.SymbolRanges);
				has_ranges = true;
			}
			ranges.Sort ();
		}

		public bool HasRanges {
			get {
				return has_ranges;
			}
		}

		public ISymbolRange[] SymbolRanges {
			get {
				if (!has_ranges)
					throw new InvalidOperationException ();
				ISymbolRange[] retval = new ISymbolRange [ranges.Count];
				ranges.CopyTo (retval, 0);
				return retval;
			}
		}

		void update_symbols ()
		{
			symbols = new ArrayList ();
			foreach (ISymbolTable symtab in symtabs) {
				if (!symtab.IsLoaded || !symtab.HasSymbols)
					continue;

				symbols.AddRange (symtab.Symbols);
				has_symbols = true;
			}
			symbols.Sort ();
		}

		public bool HasSymbols {
			get {
				return has_symbols;
			}
		}

		public ISymbol[] Symbols {
			get {
				if (!has_symbols)
					throw new InvalidOperationException ();
				ISymbol[] retval = new ISymbol [symbols.Count];
				symbols.CopyTo (retval, 0);
				return retval;
			}
		}

		public IMethod Lookup (TargetAddress address)
		{
			foreach (ISymbolTable symtab in symtabs) {
				if (!symtab.IsLoaded)
					continue;

				IMethod method = symtab.Lookup (address);

				if (method != null)
					return method;
			}

			return null;
		}

		public ISymbol Lookup (string name)
		{
			if (!HasSymbols)
				return null;

			foreach (ISymbol symbol in Symbols)
				if (symbol.Name == name)
					return symbol;

			return null;
		}

		public bool IsLoaded {
			get {
				return true;
			}
		}

		void update_handler ()
		{
			if (in_update)
				return;

			update_ranges ();
			update_symbols ();

			if (SymbolTableChanged != null)
				SymbolTableChanged ();
		}

		public void UpdateSymbolTable ()
		{
			in_update = true;
			foreach (ISymbolTable symtab in symtabs)
				symtab.UpdateSymbolTable ();
			in_update = false;

			update_handler ();
		}

		public event SymbolTableChangedHandler SymbolTableChanged;

		//
		// ICollection
		//

		int ICollection.Count {
			get {
				return symtabs.Count;
			}
		}

		bool ICollection.IsSynchronized {
			get {
				return false;
			}
		}

		object ICollection.SyncRoot {
			get {
				throw new NotSupportedException ();
			}
		}

		void ICollection.CopyTo (Array dest, int dest_idx)
		{
			symtabs.CopyTo (dest, dest_idx);
		}

		IEnumerator IEnumerable.GetEnumerator ()
		{
			return symtabs.GetEnumerator ();
		}
	}
}
