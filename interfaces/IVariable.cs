using System;

namespace Mono.Debugger
{
	// <summary>
	//   This interface provides information about a variable in the target application.
	// </summary>
	public interface IVariable
	{
		string Name {
			get;
		}

		ITargetType Type {
			get;
		}

		ITargetObject GetObject (IStackFrame frame);
	}
}
