using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public abstract class MethodSource : IMethodSource
	{
		ArrayList addresses;
		WeakReference weak_source;
		int start_row, end_row;

		long start, end;
		IMethod method;

		protected MethodSource (IMethod method)
		{
			this.method = method;
			this.start = method.StartAddress.Address;
			this.end = method.EndAddress.Address;
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

		public bool IsInSameMethod (ITargetLocation target)
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
	}
}

