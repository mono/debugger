using System;

namespace Mono.Debugger
{
	public interface IStepFrame
	{
		TargetAddress Start {
			get;
		}

		TargetAddress End {
			get;
		}

		ILanguageBackend Language {
			get;
		}
	}

	// <summary>
	//   This interface denotes an address in the target's address
	//   space in a target independent way.  It is the only way to share
	//   an address between different IInferior's.
	//
	//   Basically, this interface is a handle for a TargetAddress - before
	//   you can use it, you must resolve the location to an actual address.
	//
	//   This is used to store target addresses across different invocations
	//   of the target, for instance when setting breakpoints.
	// </summary>
	public interface ITargetLocation : ICloneable
	{
		// <summary>
		//   Address of this location in the target's address space.
		// </summary>
		TargetAddress Address {
			get;
		}

		long Offset {
			get;
		}

		bool HasAddress {
			get;
		}
	}
}
