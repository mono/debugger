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

		public override TargetClassObject Parent {
			get { throw new InvalidOperationException (); }
		}

		public override TargetObject GetField (int index)
		{
			return type.GetField (Location, index);
		}

		public override void SetField (int index, TargetObject obj)
		{
			type.SetField (Location, index, obj);
		}

		internal override long GetDynamicSize (TargetBlob blob, TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}

