using System;
using System.IO;

namespace Mono.Debugger
{
	// <summary>
	//   A location in the application's source code or in an
	//   implicitly generated source code buffer, for instance a
	//   method's assembly code.
	// </summary>
	public interface ISourceLocation
	{
		// <summary>
		//   The source buffer.
		// </summary>
		ISourceBuffer Buffer {
			get;
		}
	
		// <summary>
		//   Row in this buffer.
		// </summary>
		int Row {
			get;
		}

		// <summary>
		//   Column in this buffer.
		// </summary>
		int Column {
			get;
		}

		// <summary>
		//   Number of bytes we can single-step from the current
		//   location until we're leaving this basic block.
		// </summary>
		int SourceRange {
			get;
		}

		int SourceOffset {
			get;
		}
	}
}
