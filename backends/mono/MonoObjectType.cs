using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectType : MonoType, ITargetPointerType
	{
		int size;
		TargetAddress klass_address;
		Cecil.ITypeDefinition typedef;

		public MonoObjectType (MonoSymbolFile file, Cecil.ITypeDefinition typedef,
				       int size, TargetAddress klass)
			: base (file, TargetObjectKind.Pointer)
		{
			this.size = size;
			this.klass_address = klass;
			this.typedef = typedef;

			file.MonoLanguage.AddClass (klass_address, this);
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override string Name {
			get { return typedef.FullName; }
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

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}

	}
}
