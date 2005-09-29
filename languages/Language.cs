using System;

namespace Mono.Debugger.Languages
{
	public abstract class Language : MarshalByRefObject
	{
		public abstract string Name {
			get;
		}

		public abstract ITargetInfo TargetInfo {
			get;
		}

		public abstract TargetFundamentalType IntegerType {
			get;
		}

		public abstract TargetFundamentalType LongIntegerType {
			get;
		}

		public abstract TargetFundamentalType StringType {
			get;
		}

		public abstract TargetType PointerType {
			get;
		}

		public abstract TargetType VoidType {
			get;
		}

		public abstract TargetClassType DelegateType {
			get;
		}

		public abstract TargetClassType ExceptionType {
			get;
		}

		public abstract string SourceLanguage (StackFrame frame);

		public abstract TargetType LookupType (StackFrame frame, string name);

		public abstract bool CanCreateInstance (Type type);

		public abstract TargetFundamentalObject CreateInstance (TargetAccess target,
									object value);

		public abstract TargetPointerObject CreatePointer (StackFrame frame,
								   TargetAddress address);

		public abstract TargetObject CreateObject (TargetAccess target,
							   TargetAddress address);

		public abstract TargetObject CreateNullObject (TargetAccess target,
							       TargetType type);

		public abstract TargetAddress AllocateMemory (TargetAccess target, int size);
	}
}

