using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassObject : TargetClassObject
	{
		new MonoClassInfo type;

		public MonoClassObject (MonoClassInfo type, TargetLocation location)
			: base (type.Type, location)
		{
			this.type = type;
		}

		public override TargetClassObject Parent {
			get {
				if (!type.Type.HasParent)
					return null;

				return type.GetParentObject (Location);
			}
		}

		[Command]
		public override TargetObject GetField (TargetAccess target, int index)
		{
			return type.GetField (target, Location, index);
		}

		[Command]
		public override void SetField (TargetAccess target, int index, TargetObject obj)
		{
			type.SetField (target, Location, index, obj);
		}

		internal override long GetDynamicSize (TargetBlob blob, TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
