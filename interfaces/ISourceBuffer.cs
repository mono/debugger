using System;
using System.IO;
using Mono.CSharp.Debugger;

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
		//   If true, the whole contents of this buffer can be retrieved with
		//   with `Contents' property.  This is used for automatically generated
		//   sources like disassembly.  Otherwise, `Name' should be interpreted
		//   as a file name and the GUI is responsible for loading it.
		// </summary>
		bool HasContents {
			get;
		}
	
		// <summary>
		//   The whole contents of this buffer.  This is what
		//   a source code viewer should display.
		// </summary>
		string Contents {
			get;
		}
	}
}
