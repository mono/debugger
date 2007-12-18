using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public abstract class LineNumberTable : DebuggerMarshalByRefObject
	{
		public abstract TargetAddress Lookup (int line);

		public abstract SourceAddress Lookup (TargetAddress address);

		public abstract void DumpLineNumbers ();
	}
}
