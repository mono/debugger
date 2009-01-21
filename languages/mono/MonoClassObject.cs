using System;
using System.Runtime.InteropServices;
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

		internal override TargetStructObject GetParentObject (TargetMemoryAccess target)
		{
			if (!type.HasParent || !type.IsByRef)
				return null;

			TargetStructType sparent = type.GetParentType (target);
			if (sparent == null)
				return null;

			return (TargetStructObject) sparent.GetObject (target, Location);
		}

		internal override TargetStructObject GetCurrentObject (TargetMemoryAccess target)
		{
			if (!type.IsByRef)
				return null;

			return type.GetCurrentObject (target, Location);
		}

		internal TargetAddress KlassAddress {
			get { return info.KlassAddress; }
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
