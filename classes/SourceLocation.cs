using System;
using System.Text;

namespace Mono.Debugger
{
	public class SourceLocation : ISourceLocation
	{
		ISourceBuffer SourceBuffer;
		int Row;
		int SourceRange;

		public static SourceLocation Null = new SourceLocation (null, 0);

		public SourceLocation (ISourceBuffer buffer, int row)
			: this (buffer, row, 0)
		{ }

		public SourceLocation (ISourceBuffer buffer, int row, int range)
		{
			this.SourceBuffer = buffer;
			this.Row = row;
			this.SourceRange = range;
		}

		ISourceBuffer ISourceLocation.Buffer {
			get {
				return SourceBuffer;
			}
		}

		int ISourceLocation.Row {
			get {
				return Row;
			}
		}

		int ISourceLocation.Column {
			get {
				return 0;
			}
		}

		int ISourceLocation.SourceRange {
			get {
				return SourceRange;
			}
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();
			if (SourceBuffer != null)
				builder.Append (SourceBuffer.Name);
			else
				builder.Append ("<unknown>");
			if (Row > 0) {
				builder.Append (" line ");
				builder.Append (Row);
			}
			if (SourceRange > 0) {
				builder.Append (" (range ");
				builder.Append (SourceRange);
				builder.Append (")");
			}
			
			return builder.ToString ();
		}
	}
}
