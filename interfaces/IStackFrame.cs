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

		// <summary>
		//   Private data.
		// </summary>
		object FrameHandle {
			get;
		}

		// <summary>
		//   Returns an ITargetMemoryAccess which can be used to access the frame's
		//   parameters and local variables.
		// </summary>
		ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		// <summary>
		//   This event is emitted when the frame becomes invalid.
		// </summary>
		event StackFrameInvalidHandler FrameInvalid;
	}
}
