using System;
using System.IO;

namespace Mono.Debugger
{
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

		bool IsLoaded {
			get;
		}

		ITargetLocation StartAddress {
			get;
		}

		ITargetLocation EndAddress {
			get;
		}

		bool HasSource {
			get;
		}

		IMethodSource Source {
			get;
		}
	}
}
