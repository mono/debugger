using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueType : MonoType
	{
		public MonoOpaqueType (Type type, int size)
			: base (type, size)
		{ }

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public override bool HasObject {
			get {
				return false;
			}
		}

		public override MonoObject GetObject (ITargetLocation location)
		{
			throw new InvalidOperationException ();
		}
	}
}
