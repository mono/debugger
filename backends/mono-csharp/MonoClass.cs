using System;
using System.Text;
using System.Collections;
using R = System.Reflection;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClass : MonoType, ITargetClassType
	{
		MonoFieldInfo[] fields, static_fields;
		MonoPropertyInfo[] properties, static_properties;
		MonoMethodInfo[] methods, static_methods, ctors;
		TargetBinaryReader info;
		internal readonly MonoSymbolFile File;
		bool is_valuetype;
		int num_fields, num_static_fields, num_properties, num_static_properties;
		int num_methods, num_static_methods, num_ctors, num_ifaces;
		int field_info_offset, static_field_info_offset, property_info_offset;
		int static_property_info_offset, method_info_offset, static_method_info_offset;
		int ctor_info_offset, iface_info_offset;
		int first_field, first_static_field, first_property, first_static_property;
		int first_method, first_static_method;
		long offset;

		public readonly Type Type;
		public readonly Type EffectiveType;
		public readonly int InstanceSize;
		public readonly TargetAddress KlassAddress;
		public readonly MonoClass Parent;

		protected readonly TargetAddress ClassGetStaticFieldData;

		public MonoClass (TargetObjectKind kind, Type type, int size, bool is_classinfo,
				  TargetBinaryReader info, MonoSymbolFile file, bool has_fixed_size)
			: base (kind, type, size, has_fixed_size)
		{
			this.File = file;
			if (!is_classinfo) {
			again:
				int offset = info.ReadInt32 ();
				byte[] data = file.Table.GetTypeInfo (offset);
				info = new TargetBinaryReader (data, file.Table.TargetInfo);

				TypeKind tkind = (TypeKind) info.ReadByte ();
				if ((tkind == TypeKind.Class) || (tkind == TypeKind.Struct)) {
					info.ReadInt32 ();
					goto again;
				} else if (tkind != TypeKind.ClassInfo)
					throw new InternalError ();
				info.ReadInt32 ();
				is_valuetype = info.ReadByte () != 0;
			} else {
				is_valuetype = kind == TargetObjectKind.Struct;
			}

			KlassAddress = new TargetAddress (file.Table.GlobalAddressDomain, info.ReadAddress ());
			num_fields = info.ReadInt32 ();
			field_info_offset = info.ReadInt32 ();
			num_properties = info.ReadInt32 ();
			property_info_offset = info.ReadInt32 ();
			num_methods = info.ReadInt32 ();
			method_info_offset = info.ReadInt32 ();
			num_static_fields = info.ReadInt32 ();
			static_field_info_offset = info.ReadInt32 ();
			num_static_properties = info.ReadInt32 ();
			static_property_info_offset = info.ReadInt32 ();
			num_static_methods = info.ReadInt32 ();
			static_method_info_offset = info.ReadInt32 ();
			num_ctors = info.ReadInt32 ();
			ctor_info_offset = info.ReadInt32 ();
			num_ifaces = info.ReadInt32 ();
			iface_info_offset = info.ReadInt32 ();
			int parent = info.ReadInt32 ();

			this.info = info;
			this.offset = info.Position;
			this.Type = type;
			this.InstanceSize = size;
			ClassGetStaticFieldData = file.Table.CSharpLanguage.MonoDebuggerInfo.ClassGetStaticFieldData;

			if (parent != 0)
				Parent = (MonoClass) file.Table.GetType (Type.BaseType, parent);

			if (Type.IsEnum)
				EffectiveType = typeof (System.Enum);
			else if (Type.IsArray)
				EffectiveType = typeof (System.Array);
			else
				EffectiveType = Type;
		}

		protected MonoClass (TargetObjectKind kind, Type type, int size, bool has_fixed_size,
				     MonoClass old_class)
			: base (kind, type, size, has_fixed_size)
		{
			is_valuetype = old_class.is_valuetype;
			KlassAddress = old_class.KlassAddress;
			num_fields = old_class.num_fields;
			field_info_offset = old_class.field_info_offset;
			num_static_fields = old_class.num_static_fields;
			static_field_info_offset = old_class.static_field_info_offset;
			num_properties = old_class.num_properties;
			property_info_offset = old_class.property_info_offset;
			num_static_properties = old_class.num_static_properties;
			static_property_info_offset = old_class.static_property_info_offset;
			num_methods = old_class.num_methods;
			method_info_offset = old_class.method_info_offset;
			num_static_methods = old_class.num_static_methods;
			static_method_info_offset = old_class.static_method_info_offset;
			num_ctors = old_class.num_ctors;
			ctor_info_offset = old_class.ctor_info_offset;
			num_ifaces = old_class.num_ifaces;
			iface_info_offset = old_class.iface_info_offset;
			info = old_class.info;
			offset = old_class.offset;
			this.Type = type;
			this.InstanceSize = size;
			this.File = old_class.File;
			this.Parent = old_class.Parent;

			if (Type.IsEnum)
				EffectiveType = typeof (System.Enum);
			else if (Type.IsArray)
				EffectiveType = typeof (System.Array);
			else
				EffectiveType = Type;
		}

		public static MonoClass GetClass (Type type, int size, TargetBinaryReader info,
						  MonoSymbolFile file)
		{
			bool is_valuetype = info.ReadByte () != 0;
			TargetObjectKind kind = is_valuetype ? TargetObjectKind.Struct : TargetObjectKind.Class;
			return new MonoClass (kind, type, size, true, info, file, true);
		}

		public override bool IsByRef {
			get {
				return !is_valuetype;
			}
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

		public MonoClass ParentType {
			get {
				if (!HasParent)
					throw new InvalidOperationException ();

				return Parent;
			}
		}

		ITargetClassType ITargetClassType.ParentType {
			get {
				return ParentType;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoClassObject (this, location);
		}

		public int CountFields {
			get {
				if (Parent != null)
					return Parent.CountFields + num_fields;
				else
					return num_fields;
			}
		}

		public int CountStaticFields {
			get {
				if (Parent != null)
					return Parent.CountStaticFields + num_static_fields;
				else
					return num_static_fields;
			}
		}

		public int CountProperties {
			get {
				if (Parent != null)
					return Parent.CountProperties + num_properties;
				else
					return num_properties;
			}
		}

		public int CountStaticProperties {
			get {
				if (Parent != null)
					return Parent.CountStaticProperties + num_static_properties;
				else
					return num_static_properties;
			}
		}

		public int CountMethods {
			get {
				if (Parent != null)
					return Parent.CountMethods + num_methods;
				else
					return num_methods;
			}
		}

		public int CountStaticMethods {
			get {
				if (Parent != null)
					return Parent.CountStaticMethods + num_static_methods;
				else
					return num_static_methods;
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

			info.Position = offset + field_info_offset;
			fields = new MonoFieldInfo [num_fields];

			R.FieldInfo[] mono_fields = EffectiveType.GetFields (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);
			if (mono_fields.Length != num_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT has {1}",
					mono_fields.Length, num_fields);

			if (Parent != null)
				first_field = Parent.CountFields;

			for (int i = 0; i < num_fields; i++)
				fields [i] = new MonoFieldInfo (
					this, first_field + i, mono_fields [i],
					false, info, File);
		}

		ITargetFieldInfo[] ITargetStructType.Fields {
			get {
				return Fields;
			}
		}

		protected MonoFieldInfo[] Fields {
			get {
				init_fields ();
				return fields;
			}
		}

		void init_static_fields ()
		{
			if (static_fields != null)
				return;

			info.Position = offset + static_field_info_offset;

			static_fields = new MonoFieldInfo [num_static_fields];

			R.FieldInfo[] mono_static_fields = EffectiveType.GetFields (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);
			if (mono_static_fields.Length != num_static_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT has {1}",
					mono_static_fields.Length, num_static_fields);

			if (Parent != null)
				first_static_field = Parent.CountStaticFields;

			for (int i = 0; i < num_static_fields; i++)
				static_fields [i] = new MonoFieldInfo (
					this, first_static_field + i, mono_static_fields [i],
					true, info, File);
		}

		ITargetFieldInfo[] ITargetStructType.StaticFields {
			get {
				return StaticFields;
			}
		}

		protected MonoFieldInfo[] StaticFields {
			get {
				init_static_fields ();
				return static_fields;
			}
		}

		protected abstract class MonoStructMember : ITargetMemberInfo
		{
			public readonly MonoClass Klass;
			public readonly R.MemberInfo MemberInfo;
			public readonly int Index;
			public readonly bool IsStatic;

			public MonoStructMember (MonoClass klass, R.MemberInfo minfo, int index, bool is_static)
			{
				this.Klass = klass;
				this.MemberInfo = minfo;
				this.Index = index;
				this.IsStatic = is_static;
			}

			public abstract MonoType Type {
				get;
			}

			ITargetType ITargetMemberInfo.Type {
				get {
					return Type;
				}
			}

			string ITargetMemberInfo.Name {
				get {
					return MemberInfo.Name;
				}
			}

			int ITargetMemberInfo.Index {
				get {
					return Index;
				}
			}

			bool ITargetMemberInfo.IsStatic {
				get {
					return IsStatic;
				}
			}

			object ITargetMemberInfo.Handle {
				get {
					return MemberInfo;
				}
			}

			protected abstract string MyToString ();

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})",
						      GetType (), Klass, Type, Index, IsStatic, MyToString ());
			}
		}

		protected class MonoFieldInfo : MonoStructMember, ITargetFieldInfo
		{
			MonoType type;
			public readonly int Offset;

			public readonly R.FieldInfo FieldInfo;

			internal MonoFieldInfo (MonoClass klass, int index, R.FieldInfo finfo, bool is_static,
						TargetBinaryReader info, MonoSymbolFile file)
				: base (klass, finfo, index, is_static)
			{
				FieldInfo = finfo;
				Offset = info.ReadInt32 ();
				type = file.Table.GetType (finfo.FieldType, info.ReadInt32 ());
			}

			public override MonoType Type {
				get { return type; }
			}

			int ITargetFieldInfo.Offset {
				get { return Offset; }
			}

			protected override string MyToString ()
			{
				return String.Format ("{0:x}", Offset);
			}
		}

		internal ITargetObject GetField (TargetLocation location, int index)
		{
			init_fields ();

			if (index < first_field)
				return Parent.GetField (location, index);
			index -= first_field;

			try {
				TargetLocation field_loc = location.GetLocationAtOffset (
					fields [index].Offset, fields [index].Type.IsByRef);

				if (field_loc.Address.IsNull)
					return null;

				return fields [index].Type.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetObject GetStaticField (StackFrame frame, int index)
		{
			init_static_fields ();

			if (index < first_static_field)
				return Parent.GetStaticField (frame, index);
			index -= first_static_field;

			try {
				TargetAddress data_address = frame.Process.CallMethod (
					ClassGetStaticFieldData, KlassAddress,
					TargetAddress.Null);

				TargetLocation location = new AbsoluteTargetLocation (
					frame, data_address);
				TargetLocation field_loc = location.GetLocationAtOffset (
					static_fields [index].Offset,
					static_fields [index].Type.IsByRef);

				return static_fields [index].Type.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		void init_properties ()
		{
			if (properties != null)
				return;

			info.Position = offset + property_info_offset;
			properties = new MonoPropertyInfo [num_properties];

			R.PropertyInfo[] mono_properties = EffectiveType.GetProperties (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			if (mono_properties.Length != num_properties)
				throw new InternalError (
					"Type.GetProperties() returns {0} properties, but the JIT has {1}",
					mono_properties.Length, num_properties);

			if (Parent != null)
				first_property = Parent.CountProperties;

			for (int i = 0; i < num_properties; i++)
				properties [i] = new MonoPropertyInfo (
					this, first_property + i, mono_properties [i],
					false, info, File);
		}

		ITargetPropertyInfo[] ITargetStructType.Properties {
			get {
				return Properties;
			}
		}

		protected MonoPropertyInfo[] Properties {
			get {
				init_properties ();
				return properties;
			}
		}

		void init_static_properties ()
		{
			if (static_properties != null)
				return;

			info.Position = offset + static_property_info_offset;

			static_properties = new MonoPropertyInfo [num_static_properties];

			R.PropertyInfo[] mono_properties = EffectiveType.GetProperties (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			if (mono_properties.Length != num_static_properties)
				throw new InternalError (
					"Type.GetProperties() returns {0} properties, but the JIT has {1}",
					mono_properties.Length, num_static_properties);

			if (Parent != null)
				first_static_property = Parent.CountStaticProperties;

			for (int i = 0; i < num_static_properties; i++)
				static_properties [i] = new MonoPropertyInfo (
					this, first_static_property + i, mono_properties [i],
					true, info, File);
		}

		ITargetPropertyInfo[] ITargetStructType.StaticProperties {
			get {
				return StaticProperties;
			}
		}

		protected MonoPropertyInfo[] StaticProperties {
			get {
				init_static_properties ();
				return static_properties;
			}
		}

		protected class MonoPropertyInfo : MonoStructMember, ITargetPropertyInfo
		{
			MonoType type;
			public readonly R.PropertyInfo PropertyInfo;
			public readonly TargetAddress Getter, Setter;
			public readonly MonoFunctionType GetterType, SetterType;

			internal MonoPropertyInfo (MonoClass klass, int index, R.PropertyInfo pinfo, bool is_static,
						   TargetBinaryReader info, MonoSymbolFile file)
				: base (klass, pinfo, index, is_static)
			{
				PropertyInfo = pinfo;
				type = file.Table.GetType (pinfo.PropertyType, info.ReadInt32 ());
				Getter = new TargetAddress (file.Table.AddressDomain, info.ReadAddress ());
				Setter = new TargetAddress (file.Table.AddressDomain, info.ReadAddress ());

				if (PropertyInfo.CanRead)
					GetterType = new MonoFunctionType (
						Klass, PropertyInfo.GetGetMethod (true),
						Getter, Type, file);
				if (PropertyInfo.CanWrite)
					SetterType = new MonoFunctionType (
						Klass, PropertyInfo.GetSetMethod (true),
						Setter, Type, file);
			}

			public override MonoType Type {
				get { return type; }
			}

			public bool CanRead {
				get {
					return PropertyInfo.CanRead;
				}
			}

			public bool CanWrite {
				get {
					return PropertyInfo.CanWrite;
				}
			}

			internal ITargetObject Get (TargetLocation location)
			{
				if (!PropertyInfo.CanRead)
					throw new InvalidOperationException ();

				ITargetFunctionObject func = GetterType.GetObject (location) as ITargetFunctionObject;
				if (func == null)
					return null;

				return func.Invoke (new MonoObject [0], false);
			}

			internal ITargetObject Get (StackFrame frame)
			{
				if (!PropertyInfo.CanRead)
					throw new InvalidOperationException ();

				return GetterType.InvokeStatic (
					frame, new MonoObject [0], false);
			}

			protected override string MyToString ()
			{
				return String.Format ("{0}:{1}", CanRead, CanWrite);
			}
		}

		internal ITargetObject GetProperty (TargetLocation location, int index)
		{
			init_properties ();

			if (index < first_property)
				return Parent.GetProperty (location, index);

			return properties [index - first_property].Get (location);
		}

		public ITargetObject GetStaticProperty (StackFrame frame, int index)
		{
			init_static_properties ();

			if (index < first_static_property)
				return Parent.GetStaticProperty (frame, index);

			return static_properties [index - first_static_property].Get (frame);
		}

		void init_methods ()
		{
			if (methods != null)
				return;

			info.Position = offset + method_info_offset;
			methods = new MonoMethodInfo [num_methods];

			R.MethodInfo[] mono_methods = EffectiveType.GetMethods (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Instance |
				R.BindingFlags.Public);

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

			if (Parent != null)
				first_method = Parent.CountMethods;

			for (int i = 0; i < num_methods; i++)
				methods [i] = new MonoMethodInfo (
					this, first_method + i, (R.MethodInfo) list [i],
					false, info, File);
		}

		ITargetMethodInfo[] ITargetStructType.Methods {
			get {
				return Methods;
			}
		}

		protected MonoMethodInfo[] Methods {
			get {
				init_methods ();
				return methods;
			}
		}

		void init_static_methods ()
		{
			if (static_methods != null)
				return;

			info.Position = offset + static_method_info_offset;
			static_methods = new MonoMethodInfo [num_static_methods];

			R.MethodInfo[] mono_methods = EffectiveType.GetMethods (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static |
				R.BindingFlags.Public);

			ArrayList list = new ArrayList ();
			for (int i = 0; i < mono_methods.Length; i++) {
				if (mono_methods [i].IsSpecialName)
					continue;

				list.Add (mono_methods [i]);
			}

			if (list.Count != num_static_methods)
				throw new InternalError (
					"Type.GetMethods() returns {0} methods, but the JIT has {1}",
					list.Count, num_static_methods);

			if (Parent != null)
				first_static_method = Parent.CountStaticMethods;

			for (int i = 0; i < num_static_methods; i++)
				static_methods [i] = new MonoMethodInfo (
					this, first_static_method + i,
					(R.MethodInfo) list [i], true, info, File);
		}

		ITargetMethodInfo[] ITargetStructType.StaticMethods {
			get {
				return StaticMethods;
			}
		}

		protected MonoMethodInfo[] StaticMethods {
			get {
				init_static_methods ();
				return static_methods;
			}
		}

		void init_ctors ()
		{
			if (ctors != null)
				return;

			info.Position = offset + ctor_info_offset;
			ctors = new MonoMethodInfo [num_ctors];

			R.ConstructorInfo[] mono_ctors = EffectiveType.GetConstructors (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Instance |
				R.BindingFlags.Public);

			ArrayList list = new ArrayList ();
			for (int i = 0; i < mono_ctors.Length; i++) {
				list.Add (mono_ctors [i]);
			}

			if (list.Count != num_ctors)
				throw new InternalError (
					"Type.GetConstructors() returns {0} ctors, but the JIT has {1}",
					list.Count, num_ctors);

			for (int i = 0; i < num_ctors; i++)
				ctors [i] = new MonoMethodInfo (
					this, i, (R.ConstructorInfo) list [i], true, info, File);
		}

		ITargetMethodInfo[] ITargetStructType.Constructors {
			get {
				return Constructors;
			}
		}

		protected MonoMethodInfo[] Constructors {
			get {
				init_ctors ();
				return ctors;
			}
		}

		protected class MonoMethodInfo : MonoStructMember, ITargetMethodInfo
		{
			public readonly R.MethodBase MethodInfo;
			public readonly MonoFunctionType FunctionType;

			internal MonoMethodInfo (MonoClass klass, int index, R.MethodBase minfo, bool is_static,
						 TargetBinaryReader info, MonoSymbolFile file)
				: base (klass, minfo, index, is_static)
			{
				MethodInfo = minfo;
				FunctionType = new MonoFunctionType (Klass, minfo, info, file);
			}

			public override MonoType Type {
				get { return FunctionType; }
			}

			ITargetFunctionType ITargetMethodInfo.Type {
				get {
					return FunctionType;
				}
			}

			string ITargetMethodInfo.FullName {
				get {
					StringBuilder sb = new StringBuilder ();
					bool first = true;
					foreach (R.ParameterInfo pinfo in MethodInfo.GetParameters ()) {
						if (first)
							first = false;
						else
							sb.Append (",");
						sb.Append (pinfo.ParameterType);
					}

					return String.Format ("{0}({1})", MethodInfo.Name, sb.ToString ());
				}
			}

			protected override string MyToString ()
			{
				return String.Format ("{0}", FunctionType);
			}
		}

		internal ITargetFunctionObject GetMethod (TargetLocation location, int index)
		{
			init_methods ();

			if (index < first_method)
				return Parent.GetMethod (location, index);

			try {
				return (ITargetFunctionObject) methods [index - first_method].FunctionType.GetObject (location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetFunctionObject GetStaticMethod (StackFrame frame, int index)
		{
			init_static_methods ();

			if (index < first_static_method)
				return Parent.GetStaticMethod (frame, index);

			try {
				return static_methods [index - first_static_method].FunctionType.GetStaticObject (frame);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetFunctionObject GetConstructor (StackFrame frame, int index)
		{
			init_ctors ();

			try {
				return ctors [index].FunctionType.GetStaticObject (frame);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}:{4}]", GetType (), Type,
					      InstanceSize, KlassAddress, Parent);
		}
	}
}
