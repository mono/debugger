using System;
using System.Text;

namespace Mono.Debugger
{
	// <summary>
	//   Holds the location in the source code corresponding to an actual machine address.
	//
	//   Note that the difference between an "address" and a "location" is that a location
	//   is basically just a name for a specific source code location while an address contains
	//   some additional information which are only valid while the corresponding method is
	//   actually loaded in memory.
	//
	//   This means that you can easily transform a SourceAddress in a SourceLocation, but
	//   not the other way around.  If you insert a breakpoint on a location, the debugger will
	//   automatically compute the actual address once the corresponding method has been loaded.
	// </summary>
	public class SourceAddress : DebuggerMarshalByRefObject
	{
		LineNumberTable source;
		int row;
		int source_offset;
		int source_range;

		public static SourceAddress Null = new SourceAddress (null, 0);

		public SourceAddress (LineNumberTable source, int row)
			: this (source, row, 0, 0)
		{ }

		public SourceAddress (LineNumberTable source, int row, int offset, int range)
		{
			this.source = source;
			this.row = row;
			this.source_offset = offset;
			this.source_range = range;

			if ((source != null) && (row == 0))
				throw new InvalidOperationException ();
		}

		public SourceLocation Location {
			get {
				return new SourceLocation (source.SourceMethod, row);
			}
		}

		public LineNumberTable LineNumberTable {
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
				if (!source.IsDynamic)
					return String.Format (
						"{0}:{1}", source.SourceMethod.SourceFile.FileName, Row);
				else
					return String.Format ("{0}", Row);
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
