using System;

namespace Mono.Debugger
{
	public interface ITargetClassType : ITargetStructType
	{
		bool HasParent {
			get;
		}

		ITargetClassType ParentType {
			get;
		}
	}
}
