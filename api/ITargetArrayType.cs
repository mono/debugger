using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetArrayType : ITargetType
	{
		int Rank {
			get;
		}

		ITargetType ElementType {
			get;
		}
	}
}
