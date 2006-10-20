using System;

namespace Mono.Debugger.Interface
{
	public interface IMethodSource
	{
		string Name {
			get;
		}

		bool IsDynamic {
			get;
		}

		ISourceBuffer SourceBuffer {
			get;
		}

		ISourceFile SourceFile {
			get;
		}

		int StartRow {
			get;
		}

		int EndRow {
			get;
		}
	}
}
