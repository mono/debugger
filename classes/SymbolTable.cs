using System;
using System.Text;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public abstract class SymbolTable : ISymbolTable
	{
		protected readonly bool is_continuous;
		protected readonly ITargetLocation start_address;
		protected readonly ITargetLocation end_address;

		WeakReference method_table;

		protected SymbolTable (ITargetLocation start_address, ITargetLocation end_address)
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

		protected class MethodComparer : IComparer
		{
			public static MethodComparer Comparer = new MethodComparer ();

			public int Compare (object x, object y)
			{
				IMethod a = (IMethod) x;
				IMethod b = (IMethod) y;

				return a.StartAddress.CompareTo (b.StartAddress);
			}
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
			methods.Sort (0, methods.Count, MethodComparer.Comparer);
			method_table = new WeakReference (methods);
			return methods;
		}

		public bool IsContinuous {
			get {
				return is_continuous;
			}
		}

		public ITargetLocation StartAddress {
			get {
				if (!is_continuous)
					throw new InvalidOperationException ();

				return start_address;
			}
		}

		public ITargetLocation EndAddress {
			get {
				if (!is_continuous)
					throw new InvalidOperationException ();

				return end_address;
			}
		}

		public virtual bool Lookup (ITargetLocation target, out IMethod imethod)
		{
			imethod = null;

			long address = target.Address;
			if (IsContinuous &&
			    ((address < start_address.Address) || (address >= end_address.Address)))
				return false;

			ArrayList methods = ensure_methods ();
			if (methods == null)
				return false;

			foreach (IMethod method in methods) {
				if ((address < method.StartAddress.Address) ||
				    (address >= method.EndAddress.Address))
					continue;

				imethod = method;
				return true;
			}

			return false;
		}

		public virtual bool Lookup (ITargetLocation target, out ISourceLocation source,
					    out IMethod method)
		{
			source = null;
			if (!Lookup (target, out method))
				return false;

			source = method.Lookup (target);
			return true;
		}

		public override string ToString ()
		{
			if (is_continuous)
				return String.Format ("SymbolTable({0:x},{1:x},{2})",
						      start_address, end_address, method_table != null);
			else
				return String.Format ("SymbolTable({0})", method_table != null);
		}
	}
}
