using System;

namespace Mono.Debugger
{
	public interface IStackFrame
	{
		ISourceFile SourceFile {
			get;
		}

		int Row {
			get;
		}
	}
}
