using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectType : MonoType, IMonoTypeInfo, ITargetPointerType
	{
		int size;
		TargetAddress klass_address;

		public MonoObjectType (MonoSymbolFile file, Type type, int size, TargetAddress klass)
			: base (file, TargetObjectKind.Pointer, type)
		{
			this.size = size;
			this.klass_address = klass;

			type_info = this;
			file.MonoLanguage.AddClass (klass_address, this);
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

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		ITargetType ITargetTypeInfo.Type {
			get { return this; }
		}

		MonoType IMonoTypeInfo.Type {
			get { return this; }
		}

		MonoObject IMonoTypeInfo.GetObject (TargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}

	}
}
