using System;
using System.IO;

namespace Mono.Debugger
{
	// <summary>
	//   This is a source code buffer which can be displayed by a
	//   source code viewer.
	//
	//   A source code debugger can either belong to a file on
	//   disk or be implicitly generated, for instance as the
	//   result of disassembling a function.
	// </summary>
	public interface ISourceBuffer
	{
		// <summary>
		//   The name of this buffer.
		// </summary>
		string Name {
			get;
		}

		// <summary>
		//   The whole contents of this buffer.  This is what
		//   a source code viewer should display.
		// </summary>
		string[] Contents {
			get;
		}
	}
}
