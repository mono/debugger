using System;

namespace Mono.Debugger
{
	public interface ITargetClassObject : ITargetStructObject
	{
		ITargetClassType Type {
			get;
		}

		ITargetClassObject Parent {
			get;
		}
	}
}
