using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceObject : TargetGenericInstanceObject
	{
		new MonoGenericInstanceType type;

		public MonoGenericInstanceObject (MonoGenericInstanceType type,
						  MonoClassInfo info, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		internal override TargetClassObject GetParentObject (TargetMemoryAccess target)
		{
			if (!type.HasParent || !type.IsByRef)
				return null;

			TargetClassType sparent = type.GetParentType (target);
			if (sparent == null)
				return null;

			return (TargetClassObject) sparent.GetObject (target, Location);
		}

		internal override TargetClassObject GetCurrentObject (TargetMemoryAccess target)
		{
			if (!type.IsByRef)
				return null;

			return type.GetCurrentObject (target, Location);
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

