using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeOpaqueType : NativeType, ITargetPointerType
	{
		public NativeOpaqueType (string name, int size)
			: base (name, TargetObjectKind.Unknown, size)
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

		public override NativeObject GetObject (TargetLocation location)
		{
			throw new InvalidOperationException ();
		}
	}
}
