using System;
using System.Text;

namespace Mono.Debugger
{
	public class SourceLocation : ISourceLocation
	{
		ISourceBuffer SourceBuffer;
		int Row;

		public static SourceLocation Null = new SourceLocation (null, 0);

		public SourceLocation (ISourceBuffer buffer, int row)
		{
			this.SourceBuffer = buffer;
			this.Row = row;
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
			
			return builder.ToString ();
		}
	}
}
