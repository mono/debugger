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

		TargetAddress start, end;
		IMethod method;

		protected MethodSource (IMethod method)
		{
			this.method = method;
			this.start = method.StartAddress;
			this.end = method.EndAddress;
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

		public bool IsInSameMethod (TargetAddress address)
		{
			if ((address < start) || (address >= end))
				return false;

			return true;
		}

		public ISourceLocation Lookup (TargetAddress address)
		{
			if (!IsInSameMethod (address))
				return null;

			ISourceBuffer source = ReadSource ();
			if (source == null)
				return null;

			TargetAddress next_address = end;

			for (int i = addresses.Count-1; i >= 0; i--) {
				LineEntry entry = (LineEntry) addresses [i];

				int range = (int) (next_address - address);
				next_address = entry.Address;

				if (next_address > address)
					continue;

				int offset = (int) (address - next_address);

				return new SourceLocation (source, entry.Line, offset, range);
			}

			return null;
		}
	}
}

