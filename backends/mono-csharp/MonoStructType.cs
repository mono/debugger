using System;
using System.Reflection;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructType : MonoType, ITargetStructType
	{
		MonoFieldInfo[] fields;
		MonoPropertyInfo[] properties;
		ITargetMemoryReader info;
		internal readonly MonoSymbolFileTable Table;
		int num_fields, num_properties;
		int field_info_size, property_info_size;
		long offset;
		bool is_byref;

		protected readonly TargetAddress invoke_method;

		public MonoStructType (Type type, int size, ITargetMemoryReader info,
				       MonoSymbolFileTable table)
			: base (type, size, true)
		{
			Console.WriteLine ("STRUCT TYPE: {0}", type);
			is_byref = info.ReadByte () != 0;
			num_fields = info.BinaryReader.ReadInt32 ();
			field_info_size = info.BinaryReader.ReadInt32 ();
			num_properties = info.BinaryReader.ReadInt32 ();
			property_info_size = info.BinaryReader.ReadInt32 ();
			this.info = info;
			this.offset = info.Offset;
			this.Table = table;
			info.Offset += field_info_size;
			invoke_method = table.Language.MonoDebuggerInfo.runtime_invoke;
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
				fields [i] = new MonoFieldInfo (this, i, mono_fields [i], info, Table);
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

		void init_properties ()
		{
			if (properties != null)
				return;

			info.Offset = offset + field_info_size;
			properties = new MonoPropertyInfo [num_properties];

			PropertyInfo[] mono_properties = type.GetProperties (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);

			if (mono_properties.Length != num_properties)
				throw new InternalError (
					"Type.GetProperties() returns {0} properties, but the JIT only reports {1}",
					mono_properties.Length, num_properties);

			for (int i = 0; i < num_properties; i++)
				properties [i] = new MonoPropertyInfo (
					this, i, mono_properties [i], info, Table);
		}

		public ITargetFieldInfo[] Properties {
			get {
				init_properties ();
				return properties;
			}
		}

		protected class MonoPropertyInfo : ITargetFieldInfo
		{
			public readonly MonoType Type;
			public readonly PropertyInfo PropertyInfo;
			public readonly int Index;
			public readonly TargetAddress Getter, Setter;
			public readonly MonoStructType StructType;
			public readonly MonoObjectType ObjectType;

			internal MonoPropertyInfo (MonoStructType type, int index, PropertyInfo pinfo,
						   ITargetMemoryReader info, MonoSymbolFileTable table)
			{
				StructType = type;
				Index = index;
				PropertyInfo = pinfo;
				TargetAddress type_info = info.ReadAddress ();
				if (!type_info.IsNull)
					Type = type.GetType (
						pinfo.PropertyType, info.TargetMemoryAccess, type_info, table);
				Getter = info.ReadAddress ();
				Setter = info.ReadAddress ();
				ObjectType = new MonoObjectType (Type, StructType.Table);
				Console.WriteLine ("PROPERTY OBJECT TYPE: {0}", ObjectType);
			}

			ITargetType ITargetFieldInfo.Type {
				get {
					return Type;
				}
			}

			string ITargetFieldInfo.Name {
				get {
					return PropertyInfo.Name;
				}
			}

			int ITargetFieldInfo.Index {
				get {
					return Index;
				}
			}

			object ITargetFieldInfo.FieldHandle {
				get {
					return PropertyInfo;
				}
			}

			internal ITargetObject Get (ITargetLocation location)
			{
				ITargetMemoryAccess memory;
				TargetAddress this_object = StructType.GetAddress (location, out memory);
				TargetAddress exc_object;

				IInferior inferior = memory as IInferior;
				if (inferior == null)
					throw new LocationInvalidException ();

				Console.WriteLine ("TEST: {0} {1}", Getter, this_object);

				TargetAddress retval = inferior.CallInvokeMethod (
					StructType.invoke_method, Getter, this_object,
					new TargetAddress [0], out exc_object);

				Console.WriteLine ("RETVAL: {0}", retval);

				ITargetLocation retval_loc = new RelativeTargetLocation (
					location, retval);

				MonoObjectObject obj = new MonoObjectObject (ObjectType, retval_loc);

				Console.WriteLine ("RETVAL OBJECT: {0}", obj != null);

				Console.WriteLine ("RETVAL OBJECT CONTENTS: {0}", obj);

				return obj;
			}

			public override string ToString ()
			{
				return String.Format ("MonoProperty ({0:x}:{1}:{2})",
						      Index, PropertyInfo.Name, Type);
			}
		}

		internal ITargetObject GetProperty (ITargetLocation location, int index)
		{
			init_properties ();

			return properties [index].Get (location);
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
