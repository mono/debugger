using System;
using System.Reflection;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructType : MonoType, ITargetStructType
	{
		MonoFieldInfo[] fields;
		ITargetMemoryReader info;
		MonoSymbolFileTable table;
		int num_fields;
		int field_info_size;
		long offset;
		bool is_byref;

		public MonoStructType (Type type, int size, ITargetMemoryReader info,
				       MonoSymbolFileTable table)
			: base (type, size, true)
		{
			Console.WriteLine ("STRUCT TYPE: {0}", type);
			is_byref = info.ReadByte () != 0;
			num_fields = info.BinaryReader.ReadInt32 ();
			field_info_size = info.BinaryReader.ReadInt32 ();
			this.info = info;
			this.offset = info.Offset;
			this.table = table;
			info.Offset += field_info_size;
		}

		// <remarks>
		//   We can't do this in the .ctor since a field may be of the current
		//   classes type, but the .ctor is called before the current class is
		//   inserted into the type table hash.
		// </remarks>
		void init_fields ()
		{
			if (fields != null)
				return;

			info.Offset = offset;
			fields = new MonoFieldInfo [num_fields];

			FieldInfo[] mono_fields = type.GetFields (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);
			if (mono_fields.Length != num_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT only reports {1}",
					mono_fields.Length, num_fields);

			for (int i = 0; i < num_fields; i++)
				fields [i] = new MonoFieldInfo (this, i, mono_fields [i], info, table);
		}

		public ITargetFieldInfo[] Fields {
			get {
				init_fields ();
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
						ITargetMemoryReader info, MonoSymbolFileTable table)
			{
				Index = index;
				FieldInfo = finfo;
				Offset = info.BinaryReader.ReadInt32 ();
				TargetAddress type_info = info.ReadAddress ();
				Type = type.GetType (
					finfo.FieldType, info.TargetMemoryAccess, type_info, table);
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
			init_fields ();

			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);

			try {
				ITargetLocation field_loc = new RelativeTargetLocation (
					location, address + fields [index].Offset);

				return fields [index].Type.GetObject (field_loc);
			} catch {
				throw new LocationInvalidException ();
			}
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
