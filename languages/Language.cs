using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages
{
	public abstract class Language : DebuggerMarshalByRefObject
	{
		public abstract string Name {
			get;
		}

		public abstract bool IsManaged {
			get;
		}

		internal abstract ProcessServant Process {
			get;
		}

		public abstract TargetInfo TargetInfo {
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

		public abstract TargetClassType ObjectType {
			get;
		}

		public abstract TargetClassType ArrayType {
			get;
		}

		public abstract string SourceLanguage (StackFrame frame);

		public abstract TargetType LookupType (string name);

		public abstract bool CanCreateInstance (Type type);

		public abstract TargetFundamentalObject CreateInstance (Thread target, object value);

		public abstract TargetPointerObject CreatePointer (StackFrame frame,
								   TargetAddress address);

		public abstract TargetObject CreateObject (Thread target, TargetAddress address);

		public abstract TargetObject CreateNullObject (Thread target, TargetType type);

		public abstract TargetPointerType CreatePointerType (TargetType type);
	}
}

