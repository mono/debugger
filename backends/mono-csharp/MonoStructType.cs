using System;
using System.Collections;
using System.Reflection;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructType : MonoType, ITargetStructType
	{
		MonoFieldInfo[] fields;
		MonoPropertyInfo[] properties;
		MonoMethodInfo[] methods;
		TargetBinaryReader info;
		internal readonly MonoSymbolTable Table;
		TargetAddress klass_address;
		int num_fields, num_properties, num_methods;
		int field_info_size, property_info_size, method_info_size;
		long offset;
		bool is_byref;

		protected readonly TargetAddress invoke_method;

		public MonoStructType (Type type, int size, TargetAddress klass_address,
				       TargetBinaryReader info, MonoSymbolTable table)
			: this (TargetObjectKind.Struct, type, size, klass_address, info, table)
		{ }

		protected MonoStructType (TargetObjectKind kind, Type type, int size, TargetAddress klass_address,
					  TargetBinaryReader info, MonoSymbolTable table)
			: base (kind, type, size, klass_address, true)
		{
			is_byref = kind == TargetObjectKind.Class;
			num_fields = info.ReadInt32 ();
			field_info_size = info.ReadInt32 ();
			num_properties = info.ReadInt32 ();
			property_info_size = info.ReadInt32 ();
			num_methods = info.ReadInt32 ();
			method_info_size = info.ReadInt32 ();
			this.info = info;
			this.offset = info.Position;
			this.klass_address = klass_address;
			this.Table = table;
			info.Position += field_info_size + property_info_size + method_info_size;
			invoke_method = table.Language.MonoDebuggerInfo.RuntimeInvoke;
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

			info.Position = offset;
			fields = new MonoFieldInfo [num_fields];

			FieldInfo[] mono_fields = type.GetFields (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);
			if (mono_fields.Length != num_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT has {1}",
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
						TargetBinaryReader info, MonoSymbolTable table)
			{
				Index = index;
				FieldInfo = finfo;
				Offset = info.ReadInt32 ();
				int type_info = info.ReadInt32 ();
				Type = type.GetType (finfo.FieldType, type_info, table);
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

		internal ITargetObject GetField (TargetLocation location, int index)
		{
			init_fields ();

			try {
				TargetLocation field_loc = location.GetLocationAtOffset (
					fields [index].Offset, fields [index].Type.IsByRef);

				return fields [index].Type.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		void init_properties ()
		{
			if (properties != null)
				return;

			info.Position = offset + field_info_size;
			properties = new MonoPropertyInfo [num_properties];

			PropertyInfo[] mono_properties = type.GetProperties (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);

			if (mono_properties.Length != num_properties)
				throw new InternalError (
					"Type.GetProperties() returns {0} properties, but the JIT has {1}",
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
			public readonly MonoFunctionType GetterType, SetterType;

			internal MonoPropertyInfo (MonoStructType type, int index, PropertyInfo pinfo,
						   TargetBinaryReader info, MonoSymbolTable table)
			{
				StructType = type;
				Index = index;
				PropertyInfo = pinfo;
				int type_info = info.ReadInt32 ();
				if (type_info != 0)
					Type = type.GetType (pinfo.PropertyType, type_info, table);
				Getter = new TargetAddress (table.AddressDomain, info.ReadAddress ());
				Setter = new TargetAddress (table.AddressDomain, info.ReadAddress ());

				if (PropertyInfo.CanRead)
					GetterType = new MonoFunctionType (
						type, PropertyInfo.GetGetMethod (false), Getter, Type, table);
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

			internal ITargetObject Get (TargetLocation location)
			{
				if (!PropertyInfo.CanRead)
					throw new InvalidOperationException ();

				ITargetFunctionObject func = GetterType.GetObject (location) as ITargetFunctionObject;
				if (func == null)
					return null;

				return func.Invoke (new object [0]);
			}

			public override string ToString ()
			{
				return String.Format ("MonoProperty ({0:x}:{1}:{2})",
						      Index, PropertyInfo.Name, Type);
			}
		}

		internal ITargetObject GetProperty (TargetLocation location, int index)
		{
			init_properties ();

			return properties [index].Get (location);
		}

		void init_methods ()
		{
			if (methods != null)
				return;

			info.Position = offset + field_info_size + property_info_size;
			methods = new MonoMethodInfo [num_methods];

			MethodInfo[] mono_methods = type.GetMethods (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public);

			ArrayList list = new ArrayList ();
			for (int i = 0; i < mono_methods.Length; i++) {
				if (mono_methods [i].IsSpecialName)
					continue;

				list.Add (mono_methods [i]);
			}

			if (list.Count != num_methods)
				throw new InternalError (
					"Type.GetMethods() returns {0} methods, but the JIT has {1}",
					list.Count, num_methods);

			for (int i = 0; i < num_methods; i++)
				methods [i] = new MonoMethodInfo (
					this, i, (MethodInfo) list [i], info, Table);
		}

		public ITargetMethodInfo[] Methods {
			get {
				init_methods ();
				return methods;
			}
		}

		protected class MonoMethodInfo : ITargetMethodInfo
		{
			public readonly MonoStructType StructType;
			public readonly MethodInfo MethodInfo;
			public readonly int Index;
			public readonly MonoFunctionType FunctionType;

			internal MonoMethodInfo (MonoStructType type, int index, MethodInfo minfo,
						 TargetBinaryReader info, MonoSymbolTable table)
			{
				StructType = type;
				MethodInfo = minfo;
				Index = index;
				FunctionType = new MonoFunctionType (type, minfo, info, table);
			}

			ITargetFunctionType ITargetMethodInfo.Type {
				get {
					return FunctionType;
				}
			}

			string ITargetMethodInfo.Name {
				get {
					return MethodInfo.Name;
				}
			}

			int ITargetMethodInfo.Index {
				get {
					return Index;
				}
			}

			public override string ToString ()
			{
				return String.Format ("MonoMethod ({0:x}:{1}:{2})", Index, MethodInfo.Name, FunctionType);
			}
		}

		internal ITargetFunctionObject GetMethod (TargetLocation location, int index)
		{
			init_methods ();

			try {
				return (ITargetFunctionObject) methods [index].FunctionType.GetObject (location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public override bool IsByRef {
			get {
				return is_byref;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoStructObject (this, location);
		}

		public string PrintObject (TargetLocation location)
		{
			// Can't automatically box valuetypes yet.
			if (!(this is MonoClassType))
				throw new LocationInvalidException ();

			MonoMethodInfo method = ObjectToString as MonoMethodInfo;
			if (method == null)
				throw new InternalError ();

			ITargetObject obj = method.FunctionType.Invoke (location, new ITargetObject [0]);
			return (string) ((ITargetFundamentalObject) obj).Object;
		}
	}
}
