using System;
using System.Collections;

namespace Mono.Debugger
{
	public class SimpleSymbolTableCollection : ISimpleSymbolTable, ICollection
	{
		ArrayList symtabs = new ArrayList ();

		public void AddSymbolTable (ISimpleSymbolTable symtab)
		{
			if (symtab == null)
				return;
			symtabs.Add (symtab);
		}

		//
		// ISimpleSymbolTable
		//

		string ISimpleSymbolTable.SimpleLookup (TargetAddress address, bool exact_match)
		{
			foreach (ISimpleSymbolTable symtab in symtabs) {
				string name = symtab.SimpleLookup (address, exact_match);
				if (name != null)
					return name;
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
