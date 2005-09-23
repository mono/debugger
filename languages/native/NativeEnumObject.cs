using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeEnumObject : NativeObject, ITargetEnumObject
	{
		new NativeEnumType type;

		public NativeEnumObject (NativeEnumType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		new public ITargetEnumType Type {
			get {
				return type;
			}
		}

		public ITargetObject Value {
			get {
				return type.GetObject (location);
			}
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
