using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class SymbolTableCollection : ISymbolTableCollection
	{
		ArrayList symtabs = new ArrayList ();
		ArrayList ranges = new ArrayList ();

		public void AddSymbolTable (ISymbolTable symtab)
		{
			symtabs.Add (symtab);
			update_ranges ();
		}

		public bool IsContinuous {
			get {
				return false;
			}
		}

		public ITargetLocation StartAddress {
			get {
				throw new InvalidOperationException ();
			}
		}

		public ITargetLocation EndAddress {
			get {
				throw new InvalidOperationException ();
			}
		}

		void update_ranges ()
		{
			ranges = new ArrayList ();
			foreach (ISymbolTable symtab in symtabs) {
				if (!symtab.HasRanges)
					continue;

				ranges.AddRange (symtab.SymbolRanges);
			}
			ranges.Sort ();
		}

		public bool HasRanges {
			get {
				return true;
			}
		}

		public ISymbolRange[] SymbolRanges {
			get {
				ISymbolRange[] retval = new ISymbolRange [ranges.Count];
				ranges.CopyTo (retval, 0);
				return retval;
			}
		}

		public IMethod Lookup (ITargetLocation target)
		{
			foreach (ISymbolTable symtab in symtabs) {
				IMethod method = symtab.Lookup (target);

				if (method != null)
					return method;
			}

			return null;
		}

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
