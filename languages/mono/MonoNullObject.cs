using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoNullObject : MonoObject
	{
		public MonoNullObject (MonoType type, TargetLocation location)
			: base (type, location)
		{ }

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
