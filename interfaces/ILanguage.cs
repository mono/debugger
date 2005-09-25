using System;

namespace Mono.Debugger.Languages
{
	public interface ILanguage : IDisposable
	{
		string Name {
			get;
		}

		ITargetInfo TargetInfo {
			get;
		}

		TargetFundamentalType IntegerType {
			get;
		}

		TargetFundamentalType LongIntegerType {
			get;
		}

		TargetFundamentalType StringType {
			get;
		}

		TargetType PointerType {
			get;
		}

		TargetType VoidType {
			get;
		}

		TargetClassType DelegateType {
			get;
		}

		TargetClassType ExceptionType {
			get;
		}

		string SourceLanguage (StackFrame frame);

		TargetType LookupType (StackFrame frame, string name);

		bool CanCreateInstance (Type type);

		TargetObject CreateInstance (StackFrame frame, object obj);

		TargetFundamentalObject CreateInstance (ITargetAccess target, int value);

		TargetPointerObject CreatePointer (StackFrame frame, TargetAddress address);

		TargetObject CreateObject (ITargetAccess target, TargetAddress address);

		TargetObject CreateNullObject (ITargetAccess target, TargetType type);

		TargetAddress AllocateMemory (ITargetAccess target, int size);
	}
}

