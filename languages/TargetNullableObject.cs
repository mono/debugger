using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetNullableObject : TargetObject
	{
		public new readonly TargetNullableType Type;

		internal TargetNullableObject (TargetNullableType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		internal abstract bool HasValue (TargetMemoryAccess target);

		public bool HasValue (Thread thread)
		{
			return (bool) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return HasValue (target);
			});
		}

		internal abstract TargetObject GetValue (TargetMemoryAccess target);

		public TargetObject GetValue (Thread thread)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetValue (target);
			});
		}
	}
}
