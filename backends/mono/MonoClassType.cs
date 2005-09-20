using System;
using System.Text;
using System.Collections;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassType : MonoType, ITargetClassType
	{
		MonoFieldInfo[] fields;
		MonoFieldInfo[] static_fields;
		MonoMethodInfo[] methods;
		MonoMethodInfo[] static_methods;
		MonoPropertyInfo[] properties;
		MonoPropertyInfo[] static_properties;
		MonoEventInfo[] events;
		MonoEventInfo[] static_events;
		MonoMethodInfo[] constructors;
		MonoMethodInfo[] static_constructors;

		int num_methods = 0, num_smethods = 0;
		internal int first_method = 0, first_smethod = 0;

		Cecil.ITypeDefinition type;
		MonoClassType parent_type;
		MonoClassInfo type_info;

		public MonoClassType (MonoSymbolFile file, Cecil.ITypeDefinition type)
			: base (file, TargetObjectKind.Class)
		{
			this.type = type;

			if (type.BaseType != null)
				parent_type = file.MonoLanguage.LookupMonoType (type.BaseType) as MonoClassType;
		}

		public Cecil.ITypeDefinition Type {
			get { return type; }
		}

		public override string Name {
			get { return type.FullName; }
		}

		public override bool IsByRef {
			get { return !type.IsValueType; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 2 * file.TargetInfo.TargetAddressSize; }
		}

		public bool HasParent {
			get { return parent_type != null; }
		}

		ITargetClassType ITargetClassType.ParentType {
			get { return parent_type; }
		}

		public MonoClassType ParentType {
			get { return parent_type; }
		}

		void get_fields ()
		{
			if (fields != null)
				return;

			int num_fields = 0, num_sfields = 0;

			foreach (Cecil.IFieldDefinition field in type.Fields) {
				if (field.IsStatic)
					num_sfields++;
				else
					num_fields++;
			}

			fields = new MonoFieldInfo [num_fields];
			static_fields = new MonoFieldInfo [num_sfields];

			int pos = 0, spos = 0, i = 0;
			foreach (Cecil.IFieldDefinition field in type.Fields) {
				if (field.IsStatic) {
					static_fields [spos] = new MonoFieldInfo (File, spos, i, field);
					spos++;
				} else {
					fields [pos] = new MonoFieldInfo (File, pos, i, field);
					pos++;
				}

				i++;
			}
		}

		internal MonoFieldInfo[] Fields {
			get {
				get_fields ();
				return fields;
			}
		}

		internal MonoFieldInfo[] StaticFields {
			get {
				get_fields ();
				return static_fields;
			}
		}

		ITargetFieldInfo[] ITargetStructType.Fields {
			get { return Fields; }
		}

		ITargetFieldInfo[] ITargetStructType.StaticFields {
			get { return StaticFields; }
		}

		public ITargetObject GetStaticField (ITargetAccess target, int index)
		{
			MonoClassInfo info = GetTypeInfo ();
			if (info == null)
				return null;

			return info.GetStaticField (target, index);
		}

		public int CountMethods {
			get {
				if (parent_type != null)
					return parent_type.CountMethods + num_methods;
				else
					return num_methods;
			}
		}

		public int CountStaticMethods {
			get {
				if (parent_type != null)
					return parent_type.CountStaticMethods + num_smethods;
				else
					return num_smethods;
			}
		}

		void get_methods ()
		{
			if (methods != null)
				return;

			foreach (Cecil.IMethodDefinition method in type.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				if (method.IsStatic)
					num_smethods++;
				else
					num_methods++;
			}

			methods = new MonoMethodInfo [num_methods];
			static_methods = new MonoMethodInfo [num_smethods];

			if (parent_type != null) {
				parent_type.get_methods ();
				first_method = parent_type.CountMethods;
				first_smethod = parent_type.CountStaticMethods;
			}

			int pos = 0, spos = 0;
			foreach (Cecil.IMethodDefinition method in type.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				if (method.IsStatic) {
					static_methods [spos] = new MonoMethodInfo (this, spos, method);
					spos++;
				} else {
					methods [pos] = new MonoMethodInfo (this, pos, method);
					pos++;
				}
			}
		}

		ITargetMethodInfo[] ITargetStructType.Methods {
			get {
				get_methods ();
				return methods;
			}
		}

		ITargetMethodInfo[] ITargetStructType.StaticMethods {
			get {
				get_methods ();
				return static_methods;
			}
		}

		void get_properties ()
		{
			if (properties != null)
				return;

			int num_sproperties = 0, num_properties = 0;

			foreach (Cecil.IPropertyDefinition prop in type.Properties) {
				Cecil.IMethodDefinition m = prop.GetMethod;
				if (m == null) m = prop.SetMethod;

				if (m.IsStatic)
					num_sproperties++;
				else
					num_properties++;
			}

			properties = new MonoPropertyInfo [num_properties];
			static_properties = new MonoPropertyInfo [num_sproperties];

			int pos = 0, spos = 0;
			foreach (Cecil.IPropertyDefinition prop in type.Properties) {
				Cecil.IMethodDefinition m = prop.GetMethod;
				if (m == null) m = prop.SetMethod;

				if (m.IsStatic) {
					static_properties [spos] = new MonoPropertyInfo (this, spos, prop, true);
					spos++;
				}
				else {
					properties [pos] = new MonoPropertyInfo (this, pos, prop, false);
					pos++;
				}
			}
		}

		internal MonoPropertyInfo[] Properties {
			get {
				get_properties ();
				return properties;
			}
		}

		internal MonoPropertyInfo[] StaticProperties {
			get {
				get_properties ();
				return static_properties;
			}
		}

		ITargetPropertyInfo[] ITargetStructType.Properties {
			get { return Properties; }
		}

		ITargetPropertyInfo[] ITargetStructType.StaticProperties {
			get { return StaticProperties; }
		}

		void get_events ()
		{
			if (events != null)
				return;

			int num_sevents = 0, num_events = 0;
			foreach (Cecil.IEventDefinition ev in type.Events) {
				Cecil.IMethodDefinition m = ev.AddMethod;

				if (m.IsStatic)
					num_sevents++;
				else
					num_events++;
			}

			events = new MonoEventInfo [num_events];
			static_events = new MonoEventInfo [num_sevents];

			int pos = 0, spos = 0;
			foreach (Cecil.IEventDefinition ev in type.Events) {
				Cecil.IMethodDefinition m = ev.AddMethod;

				if (m.IsStatic) {
					static_events [spos] = new MonoEventInfo (this, spos, ev, true);
					spos++;
				}
				else {
					static_events [pos] = new MonoEventInfo (this, spos, ev, false);
					pos++;
				}
			}
		}

		public ITargetEventInfo[] Events {
			get {
				get_events ();
				return events;
			}
		}

		public ITargetEventInfo[] StaticEvents {
			get {
				get_events ();
				return static_events;
			}
		}

		public ITargetObject GetStaticEvent (StackFrame frame, int index)
		{
			get_events ();
			return null;
		}

		void get_constructors ()
		{
			if (constructors != null)
				return;

			int num_ctors = 0, num_sctors = 0;

			foreach (Cecil.IMethodDefinition method in type.Constructors) {
				if (method.IsStatic)
					num_sctors++;
				else
					num_ctors++;
			}

			constructors = new MonoMethodInfo [num_ctors];
			static_constructors = new MonoMethodInfo [num_sctors];

			int pos = 0, spos = 0;
			foreach (Cecil.IMethodDefinition method in type.Constructors) {
				if (method.IsStatic) {
					static_constructors [spos] = new MonoMethodInfo (this, spos, method);
					spos++;
				} else {
					constructors [pos] = new MonoMethodInfo (this, pos, method);
					pos++;
				}
			}
		}

		public ITargetMethodInfo[] Constructors {
			get {
				get_constructors ();
				return constructors;
			}
		}

		public ITargetMethodInfo[] StaticConstructors {
			get {
				get_constructors ();
				return static_constructors;
			}
		}

		public MonoClassInfo GetTypeInfo ()
		{
			if (type_info != null)
				return type_info;
			type_info = DoGetTypeInfo ();

			if (type_info == null)
				throw new LocationInvalidException ();

			return type_info;
		}

		public bool ResolveClass (ITargetAccess target)
		{
			if (type_info != null)
				return true;

			type_info = DoGetTypeInfo ();
			if (type_info != null)
				return true;

			int token = (int) (type.MetadataToken.TokenType + type.MetadataToken.RID);
			TargetAddress klass = file.MonoLanguage.LookupClass (
				target, file.MonoImage, token);

			type_info = new MonoClassInfo (this, target, klass);
			return true;
		}

		protected MonoClassInfo DoGetTypeInfo ()
		{
			TargetBinaryReader info = file.GetTypeInfo (type);
			if (info == null)
				return null;

			info.Position = 8;
			info.ReadLeb128 ();

			return new MonoClassInfo (this, info);
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			MonoClassInfo info = GetTypeInfo ();
			return info.GetObject (location);
		}

		[Command]
		public ITargetMemberInfo FindMember (string name, bool search_static,
						     bool search_instance)
		{
			if (search_static) {
				foreach (ITargetFieldInfo field in StaticFields)
					if (field.Name == name)
						return field;

				foreach (ITargetPropertyInfo property in StaticProperties)
					if (property.Name == name)
						return property;

				foreach (ITargetEventInfo ev in StaticEvents)
					if (ev.Name == name)
						return ev;
			}

			if (search_instance) {
				foreach (ITargetFieldInfo field in Fields)
					if (field.Name == name)
						return field;

				foreach (ITargetPropertyInfo property in Properties)
					if (property.Name == name)
						return property;

				foreach (ITargetEventInfo ev in Events)
					if (ev.Name == name)
						return ev;
			}

			return null;
		}

		public static MonoType ReadMonoClass (MonoLanguageBackend language,
						      ITargetAccess target, TargetAddress address)
		{
			TargetBlob blob = target.TargetMemoryAccess.ReadMemory (
				address, language.BuiltinTypes.KlassSize);
			TargetReader reader = new TargetReader (blob, target.TargetMemoryInfo);

			TargetAddress image = reader.ReadGlobalAddress ();
			MonoSymbolFile file = language.GetImage (image);

			reader.ReadGlobalAddress ();
			TargetAddress element_class = reader.ReadGlobalAddress ();

			if (file == null)
				return null;

			reader.Offset = language.BuiltinTypes.KlassTokenOffset;
			uint token = reader.BinaryReader.ReadUInt32 ();

			reader.Offset = language.BuiltinTypes.KlassByValArgOffset;
			TargetAddress byval_data_addr = reader.ReadGlobalAddress ();
			reader.Offset += 2;
			int type = reader.ReadByte ();

			reader.Offset = language.BuiltinTypes.KlassGenericClassOffset;
			TargetAddress generic_class = reader.ReadGlobalAddress ();

			reader.Offset = language.BuiltinTypes.KlassGenericContainerOffset;
			TargetAddress generic_container = reader.ReadGlobalAddress ();

			if (!generic_class.IsNull || !generic_container.IsNull)
				return null;

			if ((type == 0x11) || (type == 0x12)) { // MONO_TYPE_(VALUETYPE|CLASS)
				Cecil.ITypeDefinition tdef;

				if ((token & 0xff000000) != 0x02000000)
					return null;

				token &= 0x00ffffff;
				tdef = (Cecil.ITypeDefinition) file.Module.LookupByToken (
					Cecil.Metadata.TokenType.TypeDef, (int) token);

				if (tdef != null)
					return file.LookupMonoType (tdef);
			} else if (type == 0x1d) { // MONO_TYPE_SZARRAY
				MonoType eklass = ReadMonoClass (language, target, element_class);
				if (eklass == null)
					return null;

				return new MonoArrayType (eklass, 1);
			} else if (type == 0x14) { // MONO_TYPE_ARRAY
				MonoType eklass = ReadMonoClass (language, target, element_class);
				if (eklass == null)
					return null;

				TargetBlob array_data = target.TargetMemoryAccess.ReadMemory (
					byval_data_addr, language.BuiltinTypes.ArrayTypeSize);

				TargetReader array_reader = new TargetReader (
					array_data, target.TargetMemoryInfo);

				array_reader.ReadGlobalAddress ();
				int rank = array_reader.ReadByte ();

				return new MonoArrayType (eklass, rank);
			}

			return null;
		}
	}
}
