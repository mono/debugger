using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoPointerObject : TargetPointerObject
	{
		public new readonly MonoPointerType Type;

		public MonoPointerObject (MonoPointerType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public override TargetType GetCurrentType (TargetMemoryAccess target)
		{
			return Type.StaticType;
		}

		public override TargetObject GetDereferencedObject (TargetMemoryAccess target)
		{
			return Type.StaticType.GetObject (target, Location);
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override TargetObject GetArrayElement (TargetMemoryAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		public override string Print (Thread target)
		{
			if (HasAddress)
				return String.Format ("{0}", GetAddress (target));
			else
				return String.Format ("{0}", Location);
		}
	}
}
