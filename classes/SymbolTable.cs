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
		ObjectCache symbol_lookup;

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

		object get_symbol_lookup (object user_data)
		{
			return GetSymbolLookup ();
		}

		public ISymbolLookup SymbolLookup {
			get {
				if (symbol_lookup == null)
					symbol_lookup = new ObjectCache
						(new ObjectCacheFunc (get_symbol_lookup), null,
						 new TimeSpan (0,1,0));

				return (ISymbolLookup) symbol_lookup.Data;
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

		ObjectCache method_table;

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

		public abstract bool HasMethods {
			get;
		}

		protected abstract ArrayList GetMethods ();

		public IMethod[] Methods {
			get {
				if (!HasMethods)
					throw new InvalidOperationException ();

				ArrayList methods = ensure_methods ();
				if (methods == null)
					return new IMethod [0];

				IMethod[] retval = new IMethod [methods.Count];
				methods.CopyTo (retval, 0);
				return retval;
			}
		}

		static int count = 0;

		object get_methods (object user_data)
		{
			ArrayList methods = GetMethods ();
			if (methods == null)
				return null;
			methods.Sort ();
			return methods;
		}

		ArrayList ensure_methods ()
		{
			if (method_table == null)
				method_table = new ObjectCache
					(new ObjectCacheFunc (get_methods), null, new TimeSpan (0,1,0));

			return (ArrayList) method_table.Data;
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
