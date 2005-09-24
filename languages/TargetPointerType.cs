using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetPointerType : TargetType
	{
		string name;
		int size;

		public TargetPointerType (ILanguage language, string name, int size)
			: base (language, TargetObjectKind.Pointer)
		{
			this.name = name;
			this.size = size;
		}

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get { return true; }
		}

		public abstract bool IsArray {
			get;
		}

		public abstract bool IsTypesafe {
			get;
		}

		public abstract bool HasStaticType {
			get;
		}

		public abstract TargetType StaticType {
			get;
		}
	}
}
