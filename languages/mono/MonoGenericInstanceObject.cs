using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceObject : TargetClassObject
	{
		new MonoGenericInstanceType type;

		public MonoGenericInstanceObject (MonoGenericInstanceType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		internal override long GetDynamicSize (Thread target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override TargetClassObject GetParentObject (Thread target)
		{
			return null;
		}

		public override TargetClassObject GetCurrentObject (Thread target)
		{
			return this;
		}

		public override TargetObject GetField (Thread target, TargetFieldInfo field)
		{
			return null;
		}

		public override void SetField (Thread target, TargetFieldInfo field,
					       TargetObject obj)
		{ }
	}
}
