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

	public abstract class MethodBase : IMethodSource, IMethod
	{
		protected ITargetLocation start, end;
		ArrayList addresses;
		WeakReference weak_source;
		int start_row, end_row;
		string image_file;
		string name;

		protected MethodBase (IMethod method)
			: this (method.Name, method.ImageFile, method.StartAddress, method.EndAddress)
		{ }

		protected MethodBase (string name, string image_file,
				      ITargetLocation start, ITargetLocation end)
		{
			this.name = name;
			this.image_file = image_file;
			this.start = start;
			this.end = end;
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

		public IMethodSource Source {
			get {
				return this;
			}
		}

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

		public bool IsInSameMethod (ITargetLocation target)
		{
			if ((target.Address < start.Address) || (target.Address >= end.Address))
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
			long next_address = end.Address;

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

		public ITargetLocation StartAddress {
			get {
				return (ITargetLocation) start.Clone ();
			}
		}

		public ITargetLocation EndAddress {
			get {
				return (ITargetLocation) end.Clone ();
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0}({1},{2},{3})", GetType (), name, start, end);
		}
	}
}
