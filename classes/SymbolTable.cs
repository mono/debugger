using System;
using System.Text;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public abstract class SymbolRangeEntry : ISymbolRange, IComparable
	{
		TargetAddress start, end;
		WeakReference weak_lookup;

		public SymbolRangeEntry (TargetAddress start, TargetAddress end)
		{
			this.start = start;
			this.end = end;
		}

		public TargetAddress StartAddress {
			get {
				return start;
			}
		}

		public TargetAddress EndAddress {
			get {
				return end;
			}
		}

		protected abstract ISymbolLookup GetSymbolLookup ();

		public ISymbolLookup SymbolLookup {
			get {
				ISymbolLookup lookup = null;
				if (weak_lookup != null) {
					try {
						lookup = (ISymbolLookup) weak_lookup.Target;
					} catch {
						weak_lookup = null;
					}
				}

				if (lookup != null)
					return lookup;

				lookup = GetSymbolLookup ();
				if (lookup == null)
					return null;
				weak_lookup = new WeakReference (lookup);
				return lookup;
			}
		}

		public int CompareTo (object obj)
		{
			SymbolRangeEntry range = (SymbolRangeEntry) obj;
			
			if (range.StartAddress < StartAddress)
				return 1;
			else if (range.StartAddress > StartAddress)
				return -1;
			else
				return 0;
		}
	}

	public abstract class SymbolTable : ISymbolTable
	{
		protected readonly bool is_continuous;
		protected readonly TargetAddress start_address;
		protected readonly TargetAddress end_address;

		WeakReference method_table;

		protected SymbolTable (TargetAddress start_address, TargetAddress end_address)
		{
			this.is_continuous = true;
			this.start_address = start_address;
			this.end_address = end_address;
		}

		protected SymbolTable ()
		{
			this.is_continuous = false;
		}

		protected SymbolTable (ISymbolContainer container)
		{
			this.is_continuous = container.IsContinuous;
			if (container.IsContinuous) {
				this.start_address = container.StartAddress;
				this.end_address = container.EndAddress;
			}
		}

		public abstract bool HasRanges {
			get;
		}

		public abstract ISymbolRange[] SymbolRanges {
			get;
		}

		protected abstract bool HasMethods {
			get;
		}

		protected abstract ArrayList GetMethods ();

		static int count = 0;

		ArrayList ensure_methods ()
		{
			ArrayList methods = null;
			if (method_table != null) {
				try {
					methods = (ArrayList) method_table.Target;
				} catch {
					method_table = null;
				}
			}

			if (methods != null)
				return methods;

			methods = GetMethods ();
			if (methods == null)
				return null;
			methods.Sort ();
			method_table = new WeakReference (methods);
			return methods;
		}

		public bool IsContinuous {
			get {
				return is_continuous;
			}
		}

		public TargetAddress StartAddress {
			get {
				if (!is_continuous)
					throw new InvalidOperationException ();

				return start_address;
			}
		}

		public TargetAddress EndAddress {
			get {
				if (!is_continuous)
					throw new InvalidOperationException ();

				return end_address;
			}
		}

		public virtual IMethod Lookup (TargetAddress address)
		{
			if (IsContinuous && ((address < start_address) || (address >= end_address)))
				return null;

			if (HasRanges) {
				if (SymbolRanges == null)
					return null;

				foreach (SymbolRangeEntry range in SymbolRanges) {
					if ((address < range.StartAddress) || (address >= range.EndAddress))
						continue;

					return range.SymbolLookup.Lookup (address);
				}

				return null;
			}

			if (!HasMethods)
				return null;

			ArrayList methods = ensure_methods ();
			if (methods == null)
				return null;

			foreach (IMethod method in methods) {
				if (!method.IsLoaded)
					continue;

				if ((address < method.StartAddress) || (address >= method.EndAddress))
					continue;

				return method;
			}

			return null;
		}

		public virtual bool IsLoaded {
			get {
				return true;
			}
		}

		public virtual void UpdateSymbolTable ()
		{
			if (SymbolTableChanged != null)
				SymbolTableChanged ();
		}

		public event SymbolTableChangedHandler SymbolTableChanged;

		public override string ToString ()
		{
			if (is_continuous)
				return String.Format ("SymbolTable({3},{0:x},{1:x},{2})",
						      start_address, end_address, method_table != null,
						      GetType ());
			else
				return String.Format ("SymbolTable({0},{1})",
						      GetType (),method_table != null);
		}
	}
}
