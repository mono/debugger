using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeOpaqueType : TargetType
	{
		string name;
		int size;

		public NativeOpaqueType (Language language, string name, int size)
			: base (language, TargetObjectKind.Unknown)
		{
			this.name = name;
			this.size = size;
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
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

		public override bool ContainsGenericParameters {
			get { return false; }
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new NativeOpaqueObject (this, location);
		}
	}
}
