using System;

namespace Mono.Debugger
{
	public delegate void StackFrameHandler (IStackFrame frame);
	public delegate void StackFramesInvalidHandler ();

	// <summary>
	//   A single stack frame.
	// </summary>
	public interface IStackFrame
	{
		// <summary>
		//   The location in the target address space.
		// </summary>
		ITargetLocation TargetLocation {
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
