using System;

namespace Mono.Debugger
{
	public interface ITargetArray : ITargetObject
	{
		int Count {
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
