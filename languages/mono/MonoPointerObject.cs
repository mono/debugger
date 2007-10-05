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

		public override TargetType GetCurrentType (Thread target)
		{
			return Type.StaticType;
		}

		public override TargetObject GetDereferencedObject (Thread target)
		{
			return Type.StaticType.GetObject (target, Location);
		}

		internal override long GetDynamicSize (Thread target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override TargetObject GetArrayElement (Thread target, int index)
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
