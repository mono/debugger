using System;
using System.IO;

namespace Mono.Debugger
{
	// <summary>
	//   Represents the source code of a method.
	// </summary>
	// <remarks>
	//   All instances of this interface are internally cached by using
	//   weak references.
	// </remarks>
	public interface IMethodSource
	{
		ISourceBuffer SourceBuffer {
			get;
		}

		int StartRow {
			get;
		}

		int EndRow {
			get;
		}

		// <summary>
		//   This is used to lookup a source line in the method.
		// </summary>
		ISourceLocation Lookup (TargetAddress target);
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

		ILanguageBackend Language {
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
		IMethodSource Source {
			get;
		}

		// <summary>
		//   The method's parameters.
		// </summary>
		IVariable[] Parameters {
			get;
		}

		// <summary>
		//   The method's local variables
		// </summary>
		IVariable[] Locals {
			get;
		}
	}
}
