using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueType : MonoType
	{
		int size;

		public MonoOpaqueType (Type type, int size)
			: base (type)
		{
			this.size = size;
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			return true;
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public override bool HasFixedSize {
			get {
				return true;
			}
		}

		public override int Size {
			get {
				return size;
			}
		}

		public override bool HasObject {
			get {
				return false;
			}
		}

		protected override object GetObject (ITargetMemoryReader target_reader)
		{
			throw new InvalidOperationException ();
		}
	}
}
