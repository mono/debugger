using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectType : TargetType, ITargetPointerType
	{
		int size;
		MonoSymbolFile file;
		Cecil.ITypeDefinition typedef;

		public MonoObjectType (MonoSymbolFile file, Cecil.ITypeDefinition typedef, int size)
			: base (file.MonoLanguage, TargetObjectKind.Pointer)
		{
			this.size = size;
			this.file = file;
			this.typedef = typedef;
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override string Name {
			get { return typedef.FullName; }
		}

		public MonoSymbolFile File {
			get { return file; }
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

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}

	}
}
