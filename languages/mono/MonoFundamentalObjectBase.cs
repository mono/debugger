using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoFundamentalObjectBase : TargetObject, ITargetFundamentalObject
	{
		new public readonly MonoFundamentalType Type;

		public MonoFundamentalObjectBase (MonoFundamentalType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public abstract object GetObject (ITargetAccess target);

		void ITargetFundamentalObject.SetObject (ITargetObject obj)
		{
			Type.SetObject (location, (TargetObject) obj);
		}

		public override string Print (ITargetAccess target)
		{
			object obj = GetObject (target);
			if (obj is IntPtr)
				return String.Format ("0x{0:x}", ((IntPtr) obj).ToInt64 ());
			else if (obj is UIntPtr)
				return String.Format ("0x{0:x}", ((UIntPtr) obj).ToUInt64 ());
			else
				return obj.ToString ();
		}
	}
}
