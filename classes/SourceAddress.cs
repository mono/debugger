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
		SourceRange? source_range;
		int row;
		int line_offset;
		int line_range;

		public SourceAddress (SourceFile file, SourceBuffer buffer, int row,
				      int line_offset, int line_range)
		{
			this.file = file;
			this.buffer = buffer;
			this.row = row;
			this.line_offset = line_offset;
			this.line_range = line_range;
		}

		public SourceAddress (SourceFile file, SourceBuffer buffer, int row,
				      int line_offset, int line_range, SourceRange? source_range)
			: this (file, buffer, row, line_offset, line_range)
		{
			this.source_range = source_range;
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

		public bool HasSourceRange {
			get {
				return source_range != null;
			}
		}

		public SourceRange SourceRange {
			get {
				return source_range.Value;
			}
		}

		public int LineRange {
			get {
				return line_range;
			}
		}

		public int LineOffset {
			get {
				return line_offset;
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
			if (LineOffset > 0) {
				builder.Append (" (");
				builder.Append ("offset ");
				builder.Append (LineOffset);
				builder.Append (")");
			}
			if (LineRange > 0) {
				builder.Append (" (");
				builder.Append ("range ");
				builder.Append (LineRange);
				builder.Append (")");
			}
			
			return builder.ToString ();
		}
	}

	[Serializable]
	public struct SourceRange
	{
		public readonly int StartLine;
		public readonly int EndLine;
		public readonly int StartColumn;
		public readonly int EndColumn;

		public SourceRange (int start_line, int end_line, int start_col, int end_col)
		{
			this.StartLine = start_line;
			this.EndLine = end_line;
			this.StartColumn = start_col;
			this.EndColumn = end_col;
		}
	}
}
