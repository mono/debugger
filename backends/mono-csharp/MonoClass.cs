using System;
using System.Text;
using System.Collections;
using System.Reflection;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClass
	{
		MonoFieldInfo[] fields;
		MonoPropertyInfo[] properties;
		MonoMethodInfo[] methods;
		TargetBinaryReader info;
		internal readonly MonoSymbolTable Table;
		bool is_valuetype;
		int num_fields, num_properties, num_methods;
		int field_info_size, property_info_size, method_info_size;
		long offset;

		public readonly Type Type;
		public readonly int InstanceSize;
		public readonly TargetAddress KlassAddress;
		public readonly MonoClass Parent;

		protected readonly TargetAddress RuntimeInvoke;

		public MonoClass (Type type, TargetAddress klass_address, int size,
				  TargetBinaryReader info, MonoSymbolTable table)
		{
			is_valuetype = info.ReadByte () != 0;
			num_fields = info.ReadInt32 ();
			field_info_size = info.ReadInt32 ();
			num_properties = info.ReadInt32 ();
			property_info_size = info.ReadInt32 ();
			num_methods = info.ReadInt32 ();
			method_info_size = info.ReadInt32 ();
			this.info = info;
			this.offset = info.Position;
			this.Type = type;
			this.KlassAddress = klass_address;
			this.InstanceSize = size;
			this.Table = table;
			RuntimeInvoke = table.Language.MonoDebuggerInfo.RuntimeInvoke;
		}

		public bool HasParent {
			get {
				return Parent != null;
			}
		}

		public bool IsValueType {
			get {
				return is_valuetype;
			}
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

			FieldInfo[] mono_fields = Type.GetFields (
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

			internal MonoFieldInfo (MonoClass klass, int index, FieldInfo finfo,
						TargetBinaryReader info, MonoSymbolTable table)
			{
				Index = index;
				FieldInfo = finfo;
				Offset = info.ReadInt32 ();
				int type_info = info.ReadInt32 ();
				Type = MonoType.GetType (finfo.FieldType, type_info, table);
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

			PropertyInfo[] mono_properties = Type.GetProperties (
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
			public readonly MonoClass Klass;
			public readonly MonoType Type;
			public readonly PropertyInfo PropertyInfo;
			public readonly int Index;
			public readonly TargetAddress Getter, Setter;
			public readonly MonoFunctionType GetterType, SetterType;

			internal MonoPropertyInfo (MonoClass klass, int index, PropertyInfo pinfo,
						   TargetBinaryReader info, MonoSymbolTable table)
			{
				Klass = klass;
				Index = index;
				PropertyInfo = pinfo;
				int type_info = info.ReadInt32 ();
				if (type_info != 0)
					Type = MonoType.GetType (pinfo.PropertyType, type_info, table);
				Getter = new TargetAddress (table.AddressDomain, info.ReadAddress ());
				Setter = new TargetAddress (table.AddressDomain, info.ReadAddress ());

				if (PropertyInfo.CanRead)
					GetterType = new MonoFunctionType (
						Klass, PropertyInfo.GetGetMethod (false), Getter, Type, table);
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

			MethodInfo[] mono_methods = Type.GetMethods (
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
			public readonly MonoClass Klass;
			public readonly MethodInfo MethodInfo;
			public readonly int Index;
			public readonly MonoFunctionType FunctionType;

			internal MonoMethodInfo (MonoClass klass, int index, MethodInfo minfo,
						 TargetBinaryReader info, MonoSymbolTable table)
			{
				Klass = klass;
				MethodInfo = minfo;
				Index = index;
				FunctionType = new MonoFunctionType (Klass, minfo, info, table);
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

			string ITargetMethodInfo.FullName {
				get {
					StringBuilder sb = new StringBuilder ();
					bool first = true;
					foreach (ParameterInfo pinfo in MethodInfo.GetParameters ()) {
						if (first)
							first = false;
						else
							sb.Append (",");
						sb.Append (pinfo.ParameterType);
					}

					return String.Format ("{0} {1}({2})", MethodInfo.ReturnType,
							      MethodInfo.Name, sb.ToString ());
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
	}
}
