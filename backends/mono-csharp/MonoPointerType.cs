using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoPointerType : MonoType, ITargetPointerType
	{
		public MonoPointerType ()
			: base (TargetObjectKind.Pointer, typeof (void), 0)
		{ }

		public override bool IsByRef {
			get {
				return true;
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

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoPointerObject (this, location);
		}
	}
}
