using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoEnumType : MonoType
	{
		int size;
		MonoType element_type;

		public MonoEnumType (Type type, ITargetMemoryAccess memory, TargetBinaryReader info)
			: base (type)
		{
			int size_field = - info.ReadInt32 ();
			if (size_field != 1 + memory.TargetAddressSize)
				throw new InternalError ();
			if (info.ReadByte () != 4)
				throw new InternalError ();

			long element_type_info = new TargetAddress (memory, info.ReadAddress ());
			element_type = GetType (type.GetElementType (), 0, memory, element_type_info);
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			return type.IsEnum;
		}

		public override bool HasFixedSize {
			get {
				return true;
			}
		}

		public override int Size {
			get {
				return element_type.Size;
			}
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		protected override MonoObject GetObject (ITargetMemoryAccess memory, ITargetLocation location)
		{
			object data = element_type.GetObject (location).Object;

			return new MonoObject (this, Enum.ToObject (type, data));
		}
	}
}
