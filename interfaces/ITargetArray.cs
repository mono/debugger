using System;

namespace Mono.Debugger
{
	public interface ITargetArray : ITargetObject
	{
		// <summary>
		//   The array's element type.  For multi-dimensional arrays,
		//   this'll return the array type itself unless this is the
		//   last dimension.
		// </summary>
		ITargetType ElementType {
			get;
		}

		int LowerBound {
			get;
		}

		int UpperBound {
			get;
		}

		ITargetObject this [int index] {
			get;
		}
	}
}
