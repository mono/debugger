using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class SymbolTableCollection : ISymbolTableCollection
	{
		ArrayList symtabs = new ArrayList();

		public void AddSymbolTable (ISymbolTable symtab)
		{
			symtabs.Add (symtab);
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

		public bool Lookup (ITargetLocation target, out IMethod method)
		{
			method = null;

			foreach (ISymbolTable symtab in symtabs)
				if (symtab.Lookup (target, out method))
					return true;

			return false;
		}

		public bool Lookup (ITargetLocation target, out ISourceLocation source,
				    out IMethod method)
		{
			source = null;
			method = null;

			foreach (ISymbolTable symtab in symtabs)
				if (symtab.Lookup (target, out source, out method))
					return true;

			return false;
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
