using System;

namespace Mono.Debugger
{
	public interface ILanguage
	{
		string Name {
			get;
		}

		ITargetFundamentalType IntegerType {
			get;
		}

		ITargetFundamentalType LongIntegerType {
			get;
		}

		ITargetPointerType PointerType {
			get;
		}
	}
}

