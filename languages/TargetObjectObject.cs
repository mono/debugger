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

		public TargetClassObject GetClassObject (Thread thread)
		{
			return (TargetClassObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object user_data) {
					return GetClassObject (target);
			}, null);
		}

		internal abstract TargetClassObject GetClassObject (TargetMemoryAccess target);

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		internal override TargetObject GetArrayElement (TargetMemoryAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		internal override string Print (TargetMemoryAccess target)
		{
			if (HasAddress)
				return String.Format ("{0} ({1})", Type.Name, GetAddress (target));
			else
				return String.Format ("{0} ({1})", Type.Name, Location);
		}
	}
}
