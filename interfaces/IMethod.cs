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
	}

	public interface IMethod
	{
		string ImageFile{
			get;
		}

		object MethodHandle {
			get;
		}

		IMethodSource Source {
			get;
		}

		bool IsInSameMethod (ITargetLocation target);

		ISourceLocation Lookup (ITargetLocation target);

		ITargetLocation StartAddress {
			get;
		}

		ITargetLocation EndAddress {
			get;
		}
	}
}
