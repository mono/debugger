using System;

namespace Mono.Debugger
{
	// <summary>
	//   This interface denotes a variable in the target application.
	// </summary>
	public interface IVariable
	{
		string Name {
			get;
		}

		ITargetType Type {
			get;
		}

		ITargetLocation Location {
			get;
		}
	}
}
