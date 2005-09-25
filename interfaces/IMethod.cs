using System;
using System.IO;

using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public enum WrapperType
	{
		None = 0,
		DelegateInvoke,
		DelegateBeginInvoke,
		DelegateEndInvoke,
		RuntimeInvoke,
		NativeToManaged,
		ManagedToNative,
		RemotingInvoke,
		RemotingInvokeWithCheck,
		XDomainInvoke,
		XDomainDispatch,
		Ldfld,
		Stfld,
		LdfldRemote,
		StfldRemote,
		Synchronized,
		DynamicMethod,
		IsInst,
		CastClass,
		ProxyIsInst,
		StelemRef,
		UnBox,
		Unknown
	}

	public interface IMethod
	{
		string Name {
			get;
		}

		string ImageFile {
			get;
		}

		object MethodHandle {
			get;
		}

		Module Module {
			get;
		}

		// <summary>
		//   StartAddress and EndAddress are only valid if this is true.
		// </summary>
		bool IsLoaded {
			get;
		}

		TargetAddress StartAddress {
			get;
		}

		TargetAddress EndAddress {
			get;
		}

		// <summary>
		//   MethodStartAddress and MethodEndAddress are only valid if this is true.
		// </summary>
		bool HasMethodBounds {
			get;
		}

		// <summary>
		//   This is the address of the actual start of the method's code, ie. just after
		//   the prologue.
		// </summary>
		TargetAddress MethodStartAddress {
			get;
		}

		// <summary>
		//   This is the address of the actual end of the method's code, ie. just before
		//   the epilogue.
		// </summary>
		TargetAddress MethodEndAddress {
			get;
		}

		// <summary>
		//   Whether this is an icall/pinvoke wrapper.
		//   WrapperAddress is only valid if this is true.
		// </summary>
		WrapperType WrapperType {
			get;
		}

		// <summary>
		//   Source is only valid if this is true.
		// </summary>
		bool HasSource {
			get;
		}

		// <remarks>
		//   This may return null if the source file could not be found.
		//
		// Note:
		//   The return value of this property is internally cached inside
		//   a weak reference, so it's highly recommended that you call this
		//   property multiple times instead of keeping a reference yourself.
		// </remarks>
		MethodSource Source {
			get;
		}

		// <summary>
		// The method's declaring type.  In an object oriented
		// language, this will be the class/struct in which
		// this method is declared.
		// </summary>
		TargetClassType DeclaringType {
			get;
		}

		// <summary>
		// True for instance methods in object oriented
		// languages.
		// </summary>
		bool HasThis {
			get;
		}

		// <summary>
		// The method's "this" pointer.
		// </summary>

		TargetVariable This {
			get;
		}

		// <summary>
		//   The method's parameters.
		// </summary>
		TargetVariable[] Parameters {
			get;
		}

		// <summary>
		//   The method's local variables
		// </summary>
		TargetVariable[] Locals {
			get;
		}

		SourceMethod GetTrampoline (ITargetMemoryAccess memory,
					    TargetAddress address);

		SimpleStackFrame UnwindStack (SimpleStackFrame frame,
					      ITargetMemoryAccess memory,
					      IArchitecture arch);

		TargetVariable GetVariableByName (string name);
	}
}
