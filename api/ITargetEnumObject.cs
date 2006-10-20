using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetEnumObject : ITargetObject
	{
		new ITargetEnumType Type {
			get;
		}

		ITargetObject Value {
			get;
		}
	}
}
