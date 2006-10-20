using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetObject
	{
		ITargetType Type {
			get;
		}

		bool IsNull {
			get;
		}

		bool HasAddress {
			get;
		}

		TargetAddress Address {
			get;
		}

		string Print (IThread target);
	}
}
