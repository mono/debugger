using System;

namespace Mono.Debugger
{
	public interface IStepFrame
	{
		ITargetLocation Start {
			get;
		}

		ITargetLocation End {
			get;
		}
	}

	// <summary>
	//   This interface denotes an address in the target's address
	//   space.  An instance of this interface can be obtained by
	//   doing a symbol lookup or by calling CreateLocation() on
	//   one of the ISourceLanguage derivatives.
	//   backend.
	// </summary>
	public interface ITargetLocation : ICloneable, IComparable
	{
		// <summary>
		//   Address of this location in the target's address space.
		// </summary>
		long Location {
			get;
		}

		// <summary>
		//   The sum of Location and Offset.
		// </summary>
		long Address {
			get;
		}

		int Offset {
			get; set;
		}

		bool IsNull {
			get;
		}
	}
}
