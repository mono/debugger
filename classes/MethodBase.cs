using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public struct LineEntry : IComparable {
		public readonly long Address;
		public readonly int Line;

		public LineEntry (long address, int line)
		{
			this.Address = address;;
			this.Line = line;
		}

		public int CompareTo (object obj)
		{
			LineEntry entry = (LineEntry) obj;

			if (entry.Address < Address)
				return 1;
			else if (entry.Address > Address)
				return -1;
			else
				return 0;
		}
	}

	public abstract class MethodBase : IMethod, IMethodSource, ISymbolLookup, IComparable
	{
		ArrayList addresses;
		WeakReference weak_source;
		int start_row, end_row;
		long start, end;
		bool is_loaded;
		bool has_source;
		string image_file;
		string name;

		protected MethodBase (string name, string image_file, long start, long end)
			: this (name, image_file)
		{
			this.start = start;
			this.end = end;
			this.is_loaded = true;
		}

		protected MethodBase (string name, string image_file)
			: this (name, image_file, true)
		{ }

		protected MethodBase (string name, string image_file, bool has_source)
		{
			this.name = name;
			this.image_file = image_file;
			this.has_source = has_source;
		}

		protected MethodBase (IMethod method)
			: this (method.Name, method.ImageFile,
				method.StartAddress.Address, method.EndAddress.Address)
		{ }

		protected void SetAddresses (long start, long end)
		{
			this.start = start;
			this.end = end;
			this.is_loaded = true;
		}

		protected ISourceBuffer ReadSource ()
		{
			ISourceBuffer source = null;
			if (weak_source != null) {
				try {
					source = (ISourceBuffer) weak_source.Target;
				} catch {
					weak_source = null;
				}
			}

			if (source != null)
				return source;

			source = ReadSource (out start_row, out end_row, out addresses);
			if (source != null)
				weak_source = new WeakReference (source);
			return source;
		}

		protected abstract ISourceBuffer ReadSource (out int start_row, out int end_row,
							     out ArrayList addresses);


		//
		// IMethod
		//

		public string Name {
			get {
				return name;
			}
		}

		public string ImageFile {
			get {
				return image_file;
			}
		}

		public abstract object MethodHandle {
			get;
		}

		public bool IsLoaded {
			get {
				return is_loaded;
			}
		}

		public ITargetLocation StartAddress {
			get {
				if (!is_loaded)
					throw new InvalidOperationException ();

				return new TargetLocation (start);
			}
		}

		public ITargetLocation EndAddress {
			get {
				if (!is_loaded)
					throw new InvalidOperationException ();

				return new TargetLocation (end);
			}
		}

		public bool HasSource {
			get {
				return has_source;
			}
		}

		public IMethodSource Source {
			get {
				if (!HasSource)
					throw new InvalidOperationException ();

				return this;
			}
		}

		//
		// IMethodSource
		//

		public ISourceBuffer SourceBuffer {
			get {
				return ReadSource ();
			}
		}

		public int StartRow {
			get {
				return start_row + 1;
			}
		}

		public int EndRow {
			get {
				return end_row;
			}
		}

		bool IsInSameMethod (ITargetLocation target)
		{
			if ((target.Address < start) || (target.Address >= end))
				return false;

			return true;
		}

		public ISourceLocation Lookup (ITargetLocation target)
		{
			if (!IsInSameMethod (target))
				return null;

			ISourceBuffer source = ReadSource ();
			if (source == null)
				return null;

			long target_address = target.Address;
			long next_address = end;

			for (int i = addresses.Count-1; i >= 0; i--) {
				LineEntry entry = (LineEntry) addresses [i];

				int range = (int) (next_address - target_address);
				next_address = entry.Address;

				if (next_address > target_address)
					continue;

				int offset = (int) (target_address - next_address);

				return new SourceLocation (source, entry.Line, offset, range);
			}

			return null;
		}

		//
		// ISourceLookup
		//

		IMethod ISymbolLookup.Lookup (ITargetLocation target)
		{
			if (!is_loaded)
				return null;

			if ((target.Address < start) || (target.Address >= end))
				return null;

			return this;
		}

		public int CompareTo (object obj)
		{
			IMethod method = (IMethod) obj;

			long address;
			try {
				address = method.StartAddress.Address;
			} catch {
				return is_loaded ? -1 : 0;
			}

			if (!is_loaded)
				return 1;

			if (address < start)
				return 1;
			else if (address > start)
				return -1;
			else
				return 0;
		}

		public override string ToString ()
		{
			return String.Format ("{0}({1},{2:x},{3:x})", GetType (), name, start, end);
		}
	}
}
