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
		//   Returns an ITargetLocation which can be used to access a local
		//   variable at offset @offset.
		// </summary>
		ITargetLocation GetLocalVariableLocation (long offset);

		// <summary>
		//   Returns an ITargetLocation which can be used to access a method
		//   parameter at offset @offset.
		// </summary>
		ITargetLocation GetParameterLocation (long offset);

		// <summary>
		//   This event is emitted when the frame becomes invalid.
		// </summary>
		event StackFrameInvalidHandler FrameInvalid;
	}
}
