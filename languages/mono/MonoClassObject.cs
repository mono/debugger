using System;
using Mono.Debugger.Backends;

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

			MonoClassInfo parent_info = info.GetParent (target);
			if (parent_info == null)
				return null;

			MonoClassType parent_type = parent_info.ClassType;

			if (!type.IsByRef && parent_type.IsByRef) {
				TargetAddress boxed = target.CallMethod (
					type.File.MonoLanguage.MonoDebuggerInfo.GetBoxedObjectMethod,
					info.KlassAddress, Location.GetAddress (target).Address);
				TargetLocation new_loc = new AbsoluteTargetLocation (boxed);
				return new MonoClassObject (parent_type, parent_info, new_loc);
			}

			return new MonoClassObject (parent_type, parent_info, Location);
		}

		public override TargetClassObject GetCurrentObject (TargetMemoryAccess target)
		{
			if (!type.IsByRef)
				return null;

			return type.GetCurrentObject (target, Location);
		}

		public override TargetObject GetField (TargetMemoryAccess target, TargetFieldInfo field)
		{
			return ((MonoFieldInfo) field).GetField (target, Location);
		}

		public override void SetField (TargetAccess target, TargetFieldInfo field,
					       TargetObject obj)
		{
			type.SetField (target, Location, field, obj);
		}

		internal TargetAddress GetKlassAddress (TargetMemoryAccess target)
		{
			MonoClassInfo info = type.ResolveClass (target, true);
			return info.KlassAddress;
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override string Print (Thread target)
		{
			if (Location.HasAddress)
				return String.Format ("{0}", Location.GetAddress (target));
			else
				return String.Format ("{0}", Location);
		}
	}
}
