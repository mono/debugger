using System;

namespace Mono.Debugger
{
	public interface ITargetArrayObject : ITargetObject
	{
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
