using System;

namespace Mono.Debugger
{
	public abstract class TargetType : ITargetType
	{
		public abstract object TypeHandle {
			get;
		}

		public abstract int Size {
			get;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2})", GetType (), TypeHandle, Size);
		}
	}
}
