using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetObjectObject : ITargetPointerObject
	{
		new ITargetObjectType Type {
			get;
		}

		ITargetClassObject GetClassObject (IThread target);
	}
}
