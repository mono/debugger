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
	}
}
