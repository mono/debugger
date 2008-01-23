using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeStructObject : TargetClassObject
	{
		public new NativeStructType type;

		public NativeStructObject (NativeStructType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		internal override TargetStructObject GetParentObject (TargetMemoryAccess target)
		{
			return null;
		}

		internal override TargetStructObject GetCurrentObject (TargetMemoryAccess target)
		{
			return null;
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		internal override string Print (TargetMemoryAccess target)
		{
			if (Location.HasAddress)
				return String.Format ("{0}", Location.GetAddress (target));
			else
				return String.Format ("{0}", Location);
		}
	}
}

