using System;

namespace Mono.Debugger
{
	// <summary>
	//   This interface denotes a type in the target application.
	// </summary>
	public interface ITargetType
	{
		object TypeHandle {
			get;
		}

		// <summary>
		//   Whether an instance of this type has a fixed size.
		// </summary>
		bool HasFixedSize {
			get;
		}

		// <summary>
		//   The size of an instance of this type or - is HasFixedSize
		//   is false - the minimum number of bytes which must be read
		//   to determine its size.
		// </summary>
		int Size {
			get;
		}

		// <summary>
		//   If true, an instance of this type can be represented as
		//   as Mono object.
		// </summary>
		bool HasObject {
			get;
		}

		// <summary>
		//   If HasObject is true, get a Mono object which is suitable
		//   to represent an instance of this type.
		// </summary>
		object GetObject (ITargetMemoryAccess memory, TargetAddress address);
	}
}
