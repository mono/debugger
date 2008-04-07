using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFunctionObject : TargetPointerObject
	{
		public new readonly NativeFunctionPointer Type;

		public NativeFunctionObject (NativeFunctionPointer type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		internal override TargetType GetCurrentType (TargetMemoryAccess target)
		{
			throw new InvalidOperationException ();
		}

		internal override TargetObject GetArrayElement (TargetMemoryAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		internal override TargetObject GetDereferencedObject (TargetMemoryAccess target)
		{
			throw new InvalidOperationException ();
		}

		internal override string Print (TargetMemoryAccess target)
		{
			TargetAddress address = GetAddress (target);
			if (address.IsNull)
				return "null";
			else
				return String.Format ("{0}", address);
		}
	}
}
