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
		// <summary>
		//   The name of this source file or source buffer in a form suitable
		//   to be presented to the user.
		// </summary>
		// <remarks>
		//   Do not use this to load a source file, use SourceFile.FileName instead.
		// </remarks>
		string Name {
			get;
		}

		// <summary>
		//   Specifies whether this method has a dynamic source code.
		// </summary>
		bool IsDynamic {
			get;
		}

		// <summary>
		//   The source code of this method.  The buffer is cached in an ObjectCache,
		//   so you should use this property to get the file contents rather than
		//   reading the source file yourself.
		// </summary>
		ISourceBuffer SourceBuffer {
			get;
		}

		// <summary>
		//   If @IsDynamic is false, the source file this method is contained in.
		// </summary>
		SourceFile SourceFile {
			get;
		}

		// <remarks>
		//   At the moment, this is only implemented for non-dynamic methods, but
		//   this limitation will go away soon.
		// </remarks>
		SourceMethod SourceMethod {
			get;
		}

		int StartRow {
			get;
		}

		int EndRow {
			get;
		}

		// <summary>
		//   This is used to find a source line by its address.
		// </summary>
		SourceAddress Lookup (TargetAddress target);

		// <summary>
		//   This is used to find the line corresponding to a line number.
		// </summary>
		TargetAddress Lookup (int line);

		// <summary>
		//   Do a method lookup and return all methods matching @query.
		// </summary>
		SourceMethod[] MethodLookup (string query);
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
		bool IsWrapper {
			get;
		}

		// <summary>
		//   If IsWrapper is true, this is the wrapped method's code.
		// </summary>
		TargetAddress WrapperAddress {
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
