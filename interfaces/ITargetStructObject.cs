using System;

namespace Mono.Debugger
{
	public interface ITargetStructObject : ITargetObject
	{
		ITargetStructType Type {
			get;
		}

		ITargetObject GetField (int index);

		ITargetObject GetProperty (int index);
	}
}
