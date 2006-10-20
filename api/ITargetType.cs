using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetType
	{
		TargetObjectKind Kind {
			get;
		}

		string Name {
			get;
		}
	}
}
