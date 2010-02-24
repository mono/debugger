using System;

namespace Mono.Debugger.Languages
{
	public class TargetNullObject : TargetObject
	{
		public TargetNullObject (TargetType type)
			: base (type, new AbsoluteTargetLocation (TargetAddress.Null))
		{ }

		public override TargetObjectKind Kind {
			get { return TargetObjectKind.Null; }
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
