using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeOpaqueObject : TargetObject
	{
		public NativeOpaqueObject (NativeOpaqueType type, TargetLocation location)
			: base (type, location)
		{ }

		internal override long GetDynamicSize (TargetAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}

