using System;

namespace Mono.Debugger.Languages
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

		ITargetFundamentalType StringType {
			get;
		}

		ITargetType PointerType {
			get;
		}

		ITargetType LookupType (StackFrame frame, string name);

		bool CanCreateInstance (Type type);

		ITargetObject CreateInstance (StackFrame frame, object obj);

		ITargetObject CreateObject (StackFrame frame, TargetAddress address);
	}
}

