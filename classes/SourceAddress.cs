using System;
using System.Text;

namespace Mono.Debugger
{
	public class SourceAddress
	{
		IMethodSource source;
		int row;
		int source_offset;
		int source_range;

		public static SourceAddress Null = new SourceAddress (null, 0);

		public SourceAddress (IMethodSource source, int row)
			: this (source, row, 0, 0)
		{ }

		public SourceAddress (IMethodSource source, int row, int offset, int range)
		{
			this.source = source;
			this.row = row;
			this.source_offset = offset;
			this.source_range = range;
		}

		public SourceLocation Location {
			get {
				if (source.IsDynamic)
					return null;

				return new SourceLocation (source.SourceMethod, row);
			}
		}

		public IMethodSource MethodSource {
			get {
				return source;
			}
		}

		public int Row {
			get {
				return row;
			}
		}

		public int Column {
			get {
				return 0;
			}
		}

		public int SourceRange {
			get {
				return source_range;
			}
		}

		public int SourceOffset {
			get {
				return source_offset;
			}
		}

		public string Name {
			get {
				return String.Format ("{0}:{1}", source.Name, Row);
			}
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();
			builder.Append (source.Name);
			if (Row > 0) {
				builder.Append (" line ");
				builder.Append (Row);
			}
			if (SourceOffset > 0) {
				builder.Append (" (");
				builder.Append ("offset ");
				builder.Append (SourceOffset);
				builder.Append (")");
			}
			if (SourceRange > 0) {
				builder.Append (" (");
				builder.Append ("range ");
				builder.Append (SourceRange);
				builder.Append (")");
			}
			
			return builder.ToString ();
		}
	}
}
