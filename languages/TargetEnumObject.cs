using System;

namespace Mono.Debugger.Languages
{
	public class TargetEnumObject : TargetObject
	{
		public new readonly TargetEnumType Type;

		internal TargetEnumObject (TargetEnumType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public TargetObject GetValue (TargetMemoryAccess target)
		{
			return Type.Value.Type.GetObject (target, Location);
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
