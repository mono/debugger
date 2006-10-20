using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetEnumType : ITargetType
	{
		bool IsFlagsEnum {
			get;
		}

		ITargetFieldInfo Value {
			get;
		}

		ITargetFieldInfo[] Members {
			get;
		}
	}
}
