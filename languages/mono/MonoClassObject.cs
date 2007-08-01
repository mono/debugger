using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassObject : TargetClassObject
	{
		new MonoClassType type;

		public MonoClassObject (MonoClassType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public override TargetClassObject GetParentObject (Thread target)
		{
			if (!type.HasParent || !type.IsByRef)
				return null;

			return type.GetParentObject (target, Location);
		}

		public override TargetClassObject GetCurrentObject (Thread target)
		{
			if (!type.IsByRef)
				return null;

			return type.GetCurrentObject (target, Location);
		}

		public override TargetObject GetField (Thread target, TargetFieldInfo field)
		{
			return type.GetField (target, Location, field);
		}

		public override void SetField (Thread target, TargetFieldInfo field,
					       TargetObject obj)
		{
			type.SetField (target, Location, field, obj);
		}

		internal TargetAddress KlassAddress {
			get { return type.MonoClassInfo.KlassAddress; }
		}

		internal override long GetDynamicSize (Thread target, TargetBlob blob,
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
