using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoEnumObject : TargetEnumObject
	{
		public new readonly MonoEnumType Type;

		public MonoEnumObject (MonoEnumType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		public override TargetObject Value {
			get {
				return Type.GetValue (Location);
			}
		}

		internal override long GetDynamicSize (TargetAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override string ToString ()
		{
			return String.Format ("{0}", GetType());
		}
	}
}
