using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueType : MonoType
	{
		public MonoOpaqueType (Type type, int size)
			: base (TargetObjectKind.Opaque, type, size)
		{ }

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoOpaqueObject (this, location);
		}
	}
}
