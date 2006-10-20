using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetObjectType : ITargetPointerType
	{
		ITargetClassType ClassType {
			get;
		}
	}
}
