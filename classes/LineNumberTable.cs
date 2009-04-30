using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public abstract class LineNumberTable : DebuggerMarshalByRefObject
	{
		public abstract bool HasMethodBounds {
			get;
		}

		public abstract TargetAddress MethodStartAddress {
			get;
		}

		public abstract TargetAddress MethodEndAddress {
			get;
		}

		public abstract TargetAddress Lookup (int line);

		public virtual TargetAddress Lookup (int line, int column)
		{
			return Lookup (line);
		}

		public abstract SourceAddress Lookup (TargetAddress address);

		public abstract void DumpLineNumbers ();
	}
}
