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

		public override TargetType GetCurrentType (TargetAccess target)
		{
			return Type.StaticType;
		}

		public override TargetObject GetDereferencedObject (TargetAccess target)
		{
			return Type.StaticType.GetObject (Location);
		}

		internal override long GetDynamicSize (TargetAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override TargetObject GetArrayElement (TargetAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		public override string Print (TargetAccess target)
		{
			if (HasAddress)
				return String.Format ("{0}", Address);
			else
				return String.Format ("{0}", Location);
		}
	}
}
