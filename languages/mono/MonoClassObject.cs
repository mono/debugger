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

		public override TargetClassObject GetParentObject (Thread target)
		{
			if (!type.HasParent || !type.IsByRef)
				return null;

			MonoClassInfo parent_info = (MonoClassInfo) info.GetParent (target);
			if (parent_info == null)
				return null;

			MonoClassType parent_type = parent_info.MonoClassType;
			if (!type.IsByRef && parent_type.IsByRef)
				return null;

			return new MonoClassObject (parent_type, parent_info, Location);
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

		public override TargetObject GetField (Thread thread, TargetFieldInfo field)
		{
			return info.GetField (thread, this, field);
		}

		public override void SetField (Thread thread, TargetFieldInfo field,
					       TargetObject obj)
		{
			info.SetField (thread, this, field, obj);
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
				return String.Format ("{0}", Location.GetAddress (target));
			else
				return String.Format ("{0}", Location);
		}
	}
}
