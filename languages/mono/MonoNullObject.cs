using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoNullObject : TargetObject
	{
		public MonoNullObject (TargetType type, TargetLocation location)
			: base (type, location)
		{ }

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
