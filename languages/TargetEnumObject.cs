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

		public TargetObject GetValue (Thread thread)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object user_data) {
					return Type.Value.Type.GetObject (target, Location);
			}, null);
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
