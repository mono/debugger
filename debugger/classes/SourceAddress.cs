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
		SourceFile file;
		SourceBuffer buffer;
		int row;
		int source_offset;
		int source_range;

		public SourceAddress (SourceFile file, SourceBuffer buffer, int row,
				      int offset, int range)
		{
			this.file = file;
			this.buffer = buffer;
			this.row = row;
			this.source_offset = offset;
			this.source_range = range;
		}

		public SourceFile SourceFile {
			get {
				return file; }
		}

		public SourceBuffer SourceBuffer {
			get {
				return buffer;
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
				if (file != null)
					return String.Format ("{0}:{1}", file.FileName, Row);
				else
					return String.Format ("{0}", Row);
			}
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();
			if (file != null)
				builder.Append (file.FileName);
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
