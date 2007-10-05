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
			return Type.GetValue (target, Location);
		}

		internal override long GetDynamicSize (Thread target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
