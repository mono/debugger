using System;

namespace Mono.Debugger
{
	// <summary>
	//   A single stack frame.
	// </summary>
	public interface IStackFrame
	{
		// <summary>
		//   The location in the target address space.
		// </summary>
		TargetAddress TargetAddress {
			get;
		}

		// <summary>
		//   The location in the application's source code.
		// </summary>
		ISourceLocation SourceLocation {
			get;
		}

		// <summary>
		//   The current method.
		// </summary>
		IMethod Method {
			get;
		}
	}
}
