using System;

namespace Mono.Debugger.Backends
{
	internal delegate object InternalTargetAccessHandler (InternalTargetAccess target,
							      object user_data);

	internal abstract class InternalTargetAccess : DebuggerMarshalByRefObject
	{
		[Obsolete("FUCK")]
		public abstract TargetMemoryAccess TargetMemoryAccess {
			get;
		}

		public abstract TargetBlob ReadMemory (TargetAddress address, int size);
	}
}
