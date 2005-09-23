using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeOpaqueType : TargetType
	{
		string name;
		int size;

		public NativeOpaqueType (ILanguage language, string name, int size)
			: base (language, TargetObjectKind.Opaque)
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
			get {
				return false;
			}
		}

		public override TargetObject GetObject (TargetLocation location)
		{
			return new NativeOpaqueObject (this, location);
		}
	}
}
