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

		public override TargetClassObject GetParentObject (TargetAccess target)
		{
			throw new InvalidOperationException ();
		}

		public override TargetObject GetField (TargetAccess target, int index)
		{
			return type.GetField (target, Location, index);
		}

		public override void SetField (TargetAccess target, int index, TargetObject obj)
		{
			type.SetField (target, Location, index, obj);
		}

		internal override long GetDynamicSize (TargetAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}

