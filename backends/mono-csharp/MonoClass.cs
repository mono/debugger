using System;
using System.Text;
using System.Collections;
using System.Reflection;
using R = System.Reflection;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClass : MonoType, ITargetClassType
	{
		MonoFieldInfo[] fields, static_fields;
		MonoPropertyInfo[] properties, static_properties;
		MonoMethodInfo[] methods, static_methods, ctors;
		TargetBinaryReader info;
		internal readonly MonoSymbolTable Table;
		bool is_valuetype;
		int num_fields, num_static_fields, num_properties, num_static_properties, num_methods, num_static_methods;
		int num_ctors, field_info_size, static_field_info_size, property_info_size, static_property_info_size;
		int method_info_size, static_method_info_size, ctor_info_size;
		long offset;

		public readonly Type Type;
		public readonly Type EffectiveType;
		public readonly int InstanceSize;
		public readonly TargetAddress KlassAddress;
		public readonly MonoClass Parent;

		// protected readonly TargetAddress StartRuntimeInvoke;
		protected readonly TargetAddress ClassGetStaticFieldData;

		public MonoClass (TargetObjectKind kind, Type type, int size, bool is_classinfo,
				  TargetBinaryReader info, MonoSymbolTable table, bool has_fixed_size)
			: base (kind, type, size, has_fixed_size)
		{
			if (!is_classinfo) {
			again:
				int offset = info.ReadInt32 ();
				byte[] data = table.GetTypeInfo (offset);
				info = new TargetBinaryReader (data, table.TargetInfo);
				TypeKind tkind = (TypeKind) info.ReadByte ();
				if ((tkind == TypeKind.Class) || (tkind == TypeKind.Struct)) {
					info.ReadInt32 ();
					goto again;
				} else if (tkind != TypeKind.ClassInfo)
					throw new InternalError ();
				int new_size = info.ReadInt32 ();
				is_valuetype = info.ReadByte () != 0;
			} else {
				is_valuetype = kind == TargetObjectKind.Struct;
			}

			KlassAddress = new TargetAddress (table.GlobalAddressDomain, info.ReadAddress ());
			num_fields = info.ReadInt32 ();
			field_info_size = info.ReadInt32 ();
			num_static_fields = info.ReadInt32 ();
			static_field_info_size = info.ReadInt32 ();
			num_properties = info.ReadInt32 ();
			property_info_size = info.ReadInt32 ();
			num_static_properties = info.ReadInt32 ();
			static_property_info_size = info.ReadInt32 ();
			num_methods = info.ReadInt32 ();
			method_info_size = info.ReadInt32 ();
			num_static_methods = info.ReadInt32 ();
			static_method_info_size = info.ReadInt32 ();
			num_ctors = info.ReadInt32 ();
			ctor_info_size = info.ReadInt32 ();
			this.info = info;
			this.offset = info.Position;
			this.Type = type;
			this.InstanceSize = size;
			this.Table = table;
			// StartRuntimeInvoke = table.Language.MonoDebuggerInfo.StartRuntimeInvoke;
			ClassGetStaticFieldData = table.Language.MonoDebuggerInfo.ClassGetStaticFieldData;

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
			field_info_size = old_class.field_info_size;
			num_static_fields = old_class.num_static_fields;
			static_field_info_size = old_class.static_field_info_size;
			num_properties = old_class.num_properties;
			property_info_size = old_class.property_info_size;
			num_static_properties = old_class.num_static_properties;
			static_property_info_size = old_class.static_property_info_size;
			num_methods = old_class.num_methods;
			method_info_size = old_class.method_info_size;
			num_static_methods = old_class.num_static_methods;
			static_method_info_size = old_class.static_method_info_size;
			num_ctors = info.ReadInt32 ();
			ctor_info_size = info.ReadInt32 ();
			info = old_class.info;
			offset = old_class.offset;
			this.Type = type;
			this.InstanceSize = size;
			this.Table = old_class.Table;
			// StartRuntimeInvoke = old_class.StartRuntimeInvoke;

			if (Type.IsEnum)
				EffectiveType = typeof (System.Enum);
			else if (Type.IsArray)
				EffectiveType = typeof (System.Array);
			else
				EffectiveType = Type;
		}

		public static MonoClass GetClass (Type type, int size, TargetBinaryReader info, MonoSymbolTable table)
		{
			bool is_valuetype = info.ReadByte () != 0;
			TargetObjectKind kind = is_valuetype ? TargetObjectKind.Struct : TargetObjectKind.Class;
			return new MonoClass (kind, type, size, true, info, table, true);
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

			FieldInfo[] mono_fields = EffectiveType.GetFields (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);
			if (mono_fields.Length != num_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT has {1}",
					mono_fields.Length, num_fields);

			for (int i = 0; i < num_fields; i++)
				fields [i] = new MonoFieldInfo (this, i, mono_fields [i], false, info, Table);
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

			info.Position = offset + field_info_size + property_info_size + method_info_size;

			static_fields = new MonoFieldInfo [num_static_fields];

			FieldInfo[] mono_static_fields = EffectiveType.GetFields (
				BindingFlags.DeclaredOnly | BindingFlags.Static |
				BindingFlags.Public | BindingFlags.NonPublic);
			if (mono_static_fields.Length != num_static_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT has {1}",
					mono_static_fields.Length, num_static_fields);

			for (int i = 0; i < num_static_fields; i++)
				static_fields [i] = new MonoFieldInfo (this, i, mono_static_fields [i], true, info, Table);
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
			public readonly MemberInfo MemberInfo;
			public readonly int Index;
			public readonly bool IsStatic;

			public MonoStructMember (MonoClass klass, MemberInfo minfo, int index, bool is_static)
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

			public readonly FieldInfo FieldInfo;

			internal MonoFieldInfo (MonoClass klass, int index, FieldInfo finfo, bool is_static,
						TargetBinaryReader info, MonoSymbolTable table)
				: base (klass, finfo, index, is_static)
			{
				FieldInfo = finfo;
				Offset = info.ReadInt32 ();
				type = table.GetType (finfo.FieldType, info.ReadInt32 ());
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

			try {
				TargetLocation field_loc = location.GetLocationAtOffset (
					fields [index].Offset, fields [index].Type.IsByRef);

				return fields [index].Type.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetObject GetStaticField (StackFrame frame, int index)
		{
			init_static_fields ();

			try {
				TargetAddress data_address = frame.Process.CallMethod (
					ClassGetStaticFieldData, KlassAddress, TargetAddress.Null);
				TargetLocation field_loc = new AbsoluteTargetLocation (frame, data_address);

				return static_fields [index].Type.GetObject (field_loc);
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

			PropertyInfo[] mono_properties = EffectiveType.GetProperties (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);

			if (mono_properties.Length != num_properties)
				throw new InternalError (
					"Type.GetProperties() returns {0} properties, but the JIT has {1}",
					mono_properties.Length, num_properties);

			for (int i = 0; i < num_properties; i++)
				properties [i] = new MonoPropertyInfo (this, i, mono_properties [i], false, info, Table);
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

			info.Position = offset + field_info_size + property_info_size + method_info_size +
				static_field_info_size;

			static_properties = new MonoPropertyInfo [num_static_properties];

			PropertyInfo[] mono_properties = EffectiveType.GetProperties (
				BindingFlags.DeclaredOnly | BindingFlags.Static |
				BindingFlags.Public | BindingFlags.NonPublic);

			if (mono_properties.Length != num_static_properties)
				throw new InternalError (
					"Type.GetProperties() returns {0} properties, but the JIT has {1}",
					mono_properties.Length, num_static_properties);

			for (int i = 0; i < num_static_properties; i++)
				static_properties [i] = new MonoPropertyInfo (this, i, mono_properties [i], true, info, Table);
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
			public readonly PropertyInfo PropertyInfo;
			public readonly TargetAddress Getter, Setter;
			public readonly MonoFunctionType GetterType, SetterType;

			internal MonoPropertyInfo (MonoClass klass, int index, PropertyInfo pinfo, bool is_static,
						   TargetBinaryReader info, MonoSymbolTable table)
				: base (klass, pinfo, index, is_static)
			{
				PropertyInfo = pinfo;
				type = table.GetType (pinfo.PropertyType, info.ReadInt32 ());
				Getter = new TargetAddress (table.AddressDomain, info.ReadAddress ());
				Setter = new TargetAddress (table.AddressDomain, info.ReadAddress ());

				if (PropertyInfo.CanRead)
					GetterType = new MonoFunctionType (
						Klass, PropertyInfo.GetGetMethod (false), Getter, Type, table);
				if (PropertyInfo.CanWrite)
					SetterType = new MonoFunctionType (
						Klass, PropertyInfo.GetSetMethod (false), Setter, Type, table);

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

				return func.Invoke (new object [0], false);
			}

			internal ITargetObject Get (StackFrame frame)
			{
				if (!PropertyInfo.CanRead)
					throw new InvalidOperationException ();

				return GetterType.InvokeStatic (frame, new object [0], false);
			}

			protected override string MyToString ()
			{
				return String.Format ("{0}:{1}", CanRead, CanWrite);
			}
		}

		internal ITargetObject GetProperty (TargetLocation location, int index)
		{
			init_properties ();

			return properties [index].Get (location);
		}

		public ITargetObject GetStaticProperty (StackFrame frame, int index)
		{
			init_static_properties ();

			return static_properties [index].Get (frame);
		}

		void init_methods ()
		{
			if (methods != null)
				return;

			info.Position = offset + field_info_size + property_info_size;
			methods = new MonoMethodInfo [num_methods];

			MethodInfo[] mono_methods = EffectiveType.GetMethods (
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
					this, i, (MethodInfo) list [i], false, info, Table);
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

			info.Position = offset + field_info_size + property_info_size + method_info_size +
				static_field_info_size + static_property_info_size;
			static_methods = new MonoMethodInfo [num_static_methods];

			MethodInfo[] mono_methods = EffectiveType.GetMethods (
				BindingFlags.DeclaredOnly | BindingFlags.Static |
				BindingFlags.Public);

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

			for (int i = 0; i < num_static_methods; i++)
				static_methods [i] = new MonoMethodInfo (
					this, i, (MethodInfo) list [i], true, info, Table);
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

			info.Position = offset + field_info_size + property_info_size + method_info_size +
				static_field_info_size + static_property_info_size + static_method_info_size;
			ctors = new MonoMethodInfo [num_ctors];

			ConstructorInfo[] mono_ctors = EffectiveType.GetConstructors (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public);

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
					this, i, (ConstructorInfo) list [i], true, info, Table);
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
						 TargetBinaryReader info, MonoSymbolTable table)
				: base (klass, minfo, index, is_static)
			{
				MethodInfo = minfo;
				FunctionType = new MonoFunctionType (Klass, minfo, info, table);
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
					foreach (ParameterInfo pinfo in MethodInfo.GetParameters ()) {
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

			try {
				return (ITargetFunctionObject) methods [index].FunctionType.GetObject (location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetFunctionObject GetStaticMethod (StackFrame frame, int index)
		{
			init_static_methods ();

			try {
				return static_methods [index].FunctionType.GetStaticObject (frame);
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
