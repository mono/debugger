using System;
using System.Reflection;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructType : MonoType, ITargetStructType
	{
		protected readonly MonoFieldInfo[] fields;
		bool is_byref;

		public MonoStructType (Type type, int size, ITargetMemoryReader info)
			: base (type, size, true)
		{
			is_byref = info.ReadByte () != 0;
			int num_fields = info.BinaryReader.ReadInt32 ();
			fields = new MonoFieldInfo [num_fields];

			FieldInfo[] mono_fields = type.GetFields (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);
			if (mono_fields.Length != num_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT only reports {1}",
					mono_fields.Length, num_fields);

			for (int i = 0; i < num_fields; i++)
				fields [i] = new MonoFieldInfo (this, i, mono_fields [i], info);
		}

		public ITargetFieldInfo[] Fields {
			get {
				return fields;
			}
		}

		protected class MonoFieldInfo : ITargetFieldInfo
		{
			public readonly MonoType Type;
			public readonly FieldInfo FieldInfo;
			public readonly int Offset;
			public readonly int Index;

			internal MonoFieldInfo (MonoStructType type, int index, FieldInfo finfo,
						ITargetMemoryReader info)
			{
				Index = index;
				FieldInfo = finfo;
				Offset = info.BinaryReader.ReadInt32 ();
				TargetAddress type_info = info.ReadAddress ();
				Type = type.GetType (finfo.FieldType, info.TargetMemoryAccess, type_info);
			}

			ITargetType ITargetFieldInfo.Type {
				get {
					return Type;
				}
			}

			string ITargetFieldInfo.Name {
				get {
					return FieldInfo.Name;
				}
			}

			int ITargetFieldInfo.Index {
				get {
					return Index;
				}
			}

			object ITargetFieldInfo.FieldHandle {
				get {
					return FieldInfo;
				}
			}

			public override string ToString ()
			{
				return String.Format ("MonoField ({0:x}:{1}:{2})",
						      Offset, FieldInfo.Name, Type);
			}
		}

		internal ITargetObject GetField (ITargetLocation location, int index)
		{
			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);

			ITargetLocation field_loc = new RelativeTargetLocation (
				location, address + fields [index].Offset);

			return fields [index].Type.GetObject (field_loc);
		}

		public override bool IsByRef {
			get {
				return is_byref;
			}
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		bool ITargetType.HasObject {
			get {
				return true;
			}
		}

		public override MonoObject GetObject (ITargetLocation location)
		{
			return new MonoStructObject (this, location);
		}
	}
}
