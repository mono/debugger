using System;

namespace Mono.Debugger
{
	public interface ITargetObject
	{
		ITargetType Type {
			get;
		}

		bool HasObject {
			get;
		}

		object Object {
			get;
		}
	}
}
