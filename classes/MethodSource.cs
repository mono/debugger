using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public abstract class MethodSource : IMethodSource
	{
		ArrayList addresses;
		ObjectCache source;
		int start_row, end_row;

		TargetAddress start, end;
		TargetAddress method_start, method_end;
		IMethod method;

		protected MethodSource (IMethod method)
		{
			this.method = method;
			this.start = method.StartAddress;
			this.end = method.EndAddress;

			if (method.HasMethodBounds) {
				method_start = method.MethodStartAddress;
				method_end = method.MethodEndAddress;
			} else {
				method_start = start;
				method_end = end;
			}
		}

		object read_source (object user_data)
		{
			return ReadSource (out start_row, out end_row, out addresses);
		}

		protected ISourceBuffer ReadSource ()
		{
			if (source == null)
				source = new ObjectCache (new ObjectCacheFunc (read_source), null,
							  new TimeSpan (0,1,0));

			return (ISourceBuffer) source.Data;
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

		public TargetAddress Lookup (int line)
		{
			ISourceBuffer source = ReadSource ();
			if ((source == null) || (line < StartRow) || (line > EndRow))
				return TargetAddress.Null;

			for (int i = 0; i < addresses.Count; i++) {
				LineEntry entry = (LineEntry) addresses [i];

				if (line <= entry.Line)
					return entry.Address;
			}

			return TargetAddress.Null;
		}

		public SourceLocation Lookup (TargetAddress address)
		{
			if (!MethodBase.IsInSameMethod (method, address))
				return null;

			ISourceBuffer source = ReadSource ();
			if (source == null)
				return null;

			if (address < method_start)
				return new SourceLocation (
					source, StartRow, (int) (address - start),
					(int) (method_start - address));
			else if (address >= method_end)
				return new SourceLocation (
					source, EndRow, (int) (address - method_end),
					(int) (end - address));			

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

