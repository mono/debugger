using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativePointerType : NativeType, ITargetPointerType
	{
		public NativePointerType (string name, int size)
			: base (name, TargetObjectKind.Pointer, size)
		{ }

		public NativePointerType (string name, NativeType target_type, int size)
			: base (name, TargetObjectKind.Pointer, size)
		{
			this.target_type = target_type;
		}

		NativeType target_type;

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
				return target_type != null;
			}
		}

		public ITargetType StaticType {
			get {
				if (target_type == null)
					throw new InvalidOperationException ();

				return target_type;
			}
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativePointerObject (this, location);
		}
	}
}
