using System;

namespace Mono.Debugger
{
	public interface ITargetObject
	{
		IVariable Variable {
			get;
		}

		ITargetLocation Location {
			get;
		}
	}
}
