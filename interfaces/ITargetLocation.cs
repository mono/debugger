using System;

namespace Mono.Debugger
{
	// <summary>
	//   This interface denotes an address in the target's address
	//   space.  An instance of this interface can be obtained by
	//   doing a symbol lookup or by calling CreateLocation() on
	//   one of the ISourceLanguage derivatives.
	//   backend.
	// </summary>
	public interface ITargetLocation
	{
		// <summary>
		//   Address of this location in the target's address space.
		// </summary>
		long Location {
			get;
		}

		void AddOffset (int offset);
	}
}
