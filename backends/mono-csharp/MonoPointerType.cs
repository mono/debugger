using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoPointerType : MonoType, ITargetPointerType
	{
		MonoType target_type;
		Type etype;
		bool is_void;

		public MonoPointerType (Type type, int size, TargetBinaryReader info, MonoSymbolFile file)
			: base (TargetObjectKind.Pointer, type, size)
		{
			etype = type.GetElementType ();
			is_void = etype == typeof (void);
			int target_type_info = info.ReadInt32 ();
			target_type = file.Table.GetType (etype, target_type_info);
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public bool IsTypesafe {
			get {
				return true;
			}
		}

		public bool HasStaticType {
			get {
				return true;
			}
		}

		public ITargetType StaticType {
			get {
				return target_type;
			}
		}

		public MonoType TargetType {
			get {
				return target_type;
			}
		}

		public bool IsVoid {
			get {
				return is_void;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoPointerObject (this, location);
		}
	}
}
