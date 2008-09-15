using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoNullObject : TargetObject
	{
		public MonoNullObject (TargetType type, TargetLocation location)
			: base (type, location)
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
