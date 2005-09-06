using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectType : MonoType, ITargetPointerType
	{
		int size;
		TargetAddress klass_address;

		public MonoObjectType (MonoSymbolFile file, Type type, int size, TargetAddress klass)
			: base (file, TargetObjectKind.Pointer, type)
		{
			this.size = size;
			this.klass_address = klass;
		}

		protected override IMonoTypeInfo CreateTypeInfo ()
		{
			return new MonoObjectTypeInfo (this, size, klass_address);
		}

		protected override IMonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			throw new InvalidOperationException ();
		}

		public override bool IsByRef {
			get { return true; }
		}

		public bool IsTypesafe {
			get { return true; }
		}

		public bool HasStaticType {
			get { return false; }
		}

		public bool IsArray {
			get { return false; }
		}

		public ITargetType StaticType {
			get {
				throw new InvalidOperationException ();
			}
		}
	}
}
