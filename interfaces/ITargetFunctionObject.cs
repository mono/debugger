using System;

namespace Mono.Debugger
{
	public interface ITargetFunctionObject : ITargetObject
	{
		ITargetFunctionType Type {
			get;
		}

		ITargetObject Invoke ();
	}
}
