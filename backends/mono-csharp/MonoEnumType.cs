using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoEnumType : MonoFundamentalType
	{
		MonoType element_type;

		public MonoEnumType (Type type, int size, TargetBinaryReader info, MonoSymbolFile file)
			: base (type, size, info, file)
		{
			int element_type_info = info.ReadInt32 ();
			element_type = file.Table.GetType (type.GetElementType (), element_type_info);
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			return type.IsEnum;
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

		public override MonoObject GetObject (TargetLocation location)
		{
			MonoObject obj = element_type.GetObject (location);
			return new MonoEnumObject (this, location, (MonoFundamentalObjectBase) obj);
		}
	}
}
