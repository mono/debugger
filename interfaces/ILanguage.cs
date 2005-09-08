using System;

namespace Mono.Debugger.Languages
{
	public interface ILanguage : IDisposable
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

		ITargetType ExceptionType {
			get;
		}

		string SourceLanguage (StackFrame frame);

		ITargetType LookupType (StackFrame frame, string name);

		bool CanCreateInstance (Type type);

		ITargetObject CreateInstance (StackFrame frame, object obj);

		ITargetFundamentalObject CreateInstance (ITargetAccess target, int value);

		ITargetPointerObject CreatePointer (StackFrame frame, TargetAddress address);

		ITargetObject CreateObject (ITargetAccess target, TargetAddress address);

		ITargetObject CreateNullObject (ITargetAccess target, ITargetType type);
	}
}

