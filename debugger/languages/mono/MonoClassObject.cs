using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassObject : TargetClassObject
	{
		new MonoClassType type;
		MonoClassInfo info;

		public MonoClassObject (MonoClassType type, MonoClassInfo info,
					TargetLocation location)
			: base (type, location)
		{
			this.type = type;
			this.info = info;
		}

		public override TargetStructObject GetParentObject (Thread thread)
		{
			return (TargetStructObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetParentObject (target);
			});
		}

		internal TargetStructObject GetParentObject (TargetMemoryAccess target)
		{
			if (!type.HasParent || !type.IsByRef)
				return null;

			TargetStructType sparent = type.GetParentType (target);
			if (sparent == null)
				return null;

			return (TargetStructObject) sparent.GetObject (target, Location);
		}

		public override TargetClassObject GetCurrentObject (Thread thread)
		{
			if (!type.IsByRef)
				return null;

			return (TargetClassObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return type.GetCurrentObject (target, Location);
			});
		}

		internal TargetAddress GetKlassAddress (TargetMemoryAccess target)
		{
			return info.KlassAddress;
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
				return String.Format ("({0}) {1}",
						      Type.Name, Location.GetAddress (target));
			else
				return String.Format ("({0}) {1}",
						      Type.Name, Location);
		}
	}
}
