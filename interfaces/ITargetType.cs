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
		object GetObject (ITargetMemoryReader reader);
	}
}
