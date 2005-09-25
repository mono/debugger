using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeEnumObject : TargetEnumObject
	{
		public NativeEnumObject (NativeEnumType type, TargetLocation location)
			: base (type, location)
		{ }

		public override TargetObject Value {
			get {
				return Type.GetObject (Location);
			}
		}

		internal override long GetDynamicSize (TargetAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
