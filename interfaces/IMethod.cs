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
		ISourceLocation Lookup (ITargetLocation target);
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

		ITargetLocation StartAddress {
			get;
		}

		ITargetLocation EndAddress {
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
	}
}
