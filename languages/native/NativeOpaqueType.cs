using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeOpaqueType : NativeType
	{
		public NativeOpaqueType (string name, int size)
			: base (name, TargetObjectKind.Opaque, size)
		{ }

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeOpaqueObject (this, location);
		}
	}
}
