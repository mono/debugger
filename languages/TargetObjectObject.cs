using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetObjectObject : TargetPointerObject
	{
		public new readonly TargetObjectType Type;

		internal TargetObjectObject (TargetObjectType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public abstract TargetClassObject GetClassObject (TargetAccess target);

		internal override long GetDynamicSize (TargetAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override TargetObject GetArrayElement (TargetAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		public override string Print (TargetAccess target)
		{
			if (HasAddress)
				return String.Format ("{0} ({1})", Type.Name, Address);
			else
				return String.Format ("{0} ({1})", Type.Name, Location);
		}
	}
}
