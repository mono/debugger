using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueType : MonoType, ITargetPointerType
	{
		public MonoOpaqueType (Type type, int size)
			: base (TargetObjectKind.Unknown, type, size)
		{ }

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public bool IsTypesafe {
			get {
				return false;
			}
		}

		public bool HasStaticType {
			get {
				return false;
			}
		}

		public ITargetType StaticType {
			get {
				throw new InvalidOperationException ();
			}
		}

		public override MonoObject GetObject (MonoTargetLocation location)
		{
			throw new InvalidOperationException ();
		}
	}
}
