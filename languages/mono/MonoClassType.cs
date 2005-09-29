using System;
using System.Text;
using System.Collections;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassType : TargetClassType
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
		MonoSymbolFile file;
		MonoClassType parent_type;
		MonoClassInfo type_info;

		public MonoClassType (MonoSymbolFile file, Cecil.ITypeDefinition type)
			: base (file.MonoLanguage, TargetObjectKind.Class)
		{
			this.type = type;
			this.file = file;

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
			get { return 2 * Language.TargetInfo.TargetAddressSize; }
		}

		public MonoSymbolFile File {
			get { return file; }
		}

		public override bool HasParent {
			get { return parent_type != null; }
		}

		public override TargetClassType ParentType {
			get { return parent_type; }
		}

		internal MonoClassType MonoParentType {
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
				TargetType ftype = File.MonoLanguage.LookupMonoType (field.FieldType);
				if (field.IsStatic) {
					static_fields [spos] = new MonoFieldInfo (ftype, spos, i, field);
					spos++;
				} else {
					fields [pos] = new MonoFieldInfo (ftype, pos, i, field);
					pos++;
				}

				i++;
			}
		}

		public override TargetFieldInfo[] Fields {
			get {
				get_fields ();
				return fields;
			}
		}

		public override TargetFieldInfo[] StaticFields {
			get {
				get_fields ();
				return static_fields;
			}
		}

		public override TargetObject GetStaticField (TargetAccess target, int index)
		{
			MonoClassInfo info = GetTypeInfo ();
			return info.GetStaticField (target, index);
		}

		public override void SetStaticField (TargetAccess target, int index,
						     TargetObject obj)
		{
			MonoClassInfo info = GetTypeInfo ();
			info.SetStaticField (target, index, obj);
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
					static_methods [spos] = MonoMethodInfo.Create (this, spos, method);
					spos++;
				} else {
					methods [pos] = MonoMethodInfo.Create (this, pos, method);
					pos++;
				}
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
					static_properties [spos] = MonoPropertyInfo.Create (
						this, spos, prop, true);
					spos++;
				}
				else {
					properties [pos] = MonoPropertyInfo.Create (
						this, pos, prop, false);
					pos++;
				}
			}
		}

		public override TargetMethodInfo[] Methods {
			get {
				get_methods ();
				return methods;
			}
		}

		public override TargetMethodInfo[] StaticMethods {
			get {
				get_methods ();
				return static_methods;
			}
		}

		public override TargetPropertyInfo[] Properties {
			get {
				get_properties ();
				return properties;
			}
		}

		public override TargetPropertyInfo[] StaticProperties {
			get {
				get_properties ();
				return static_properties;
			}
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
					static_events [spos] = MonoEventInfo.Create (this, spos, ev, true);
					spos++;
				}
				else {
					events [pos] = MonoEventInfo.Create (this, pos, ev, false);
					pos++;
				}
			}
		}

		public override TargetEventInfo[] Events {
			get {
				get_events ();
				return events;
			}
		}

		public override TargetEventInfo[] StaticEvents {
			get {
				get_events ();
				return static_events;
			}
		}

		public TargetObject GetStaticEvent (StackFrame frame, int index)
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
					static_constructors [spos] = MonoMethodInfo.Create (
						this, spos, method);
					spos++;
				} else {
					constructors [pos] = MonoMethodInfo.Create (
						this, pos, method);
					pos++;
				}
			}
		}

		public override TargetMethodInfo[] Constructors {
			get {
				get_constructors ();
				return constructors;
			}
		}

		public override TargetMethodInfo[] StaticConstructors {
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

			if (type_info != null)
				return type_info;

			throw new TargetException (
				TargetError.LocationInvalid, "Can't find class `{0}'", Name);
		}

		public override bool ResolveClass (TargetAccess target)
		{
			if (type_info != null)
				return true;

			if (parent_type != null) {
				if (!parent_type.ResolveClass (target))
					return false;
			}

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

		internal override TargetObject GetObject (TargetLocation location)
		{
			MonoClassInfo info = GetTypeInfo ();
			return info.GetObject (location);
		}

		[Command]
		public override TargetMemberInfo FindMember (string name, bool search_static,
							     bool search_instance)
		{
			if (search_static) {
				foreach (TargetFieldInfo field in StaticFields)
					if (field.Name == name)
						return field;

				foreach (TargetPropertyInfo property in StaticProperties)
					if (property.Name == name)
						return property;

				foreach (TargetEventInfo ev in StaticEvents)
					if (ev.Name == name)
						return ev;
			}

			if (search_instance) {
				foreach (TargetFieldInfo field in Fields)
					if (field.Name == name)
						return field;

				foreach (TargetPropertyInfo property in Properties)
					if (property.Name == name)
						return property;

				foreach (TargetEventInfo ev in Events)
					if (ev.Name == name)
						return ev;
			}

			return null;
		}

		public static TargetType ReadMonoClass (MonoLanguageBackend language,
							TargetAccess target, TargetAddress address)
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
				TargetType eklass = ReadMonoClass (language, target, element_class);
				if (eklass == null)
					return null;

				return new MonoArrayType (eklass, 1);
			} else if (type == 0x14) { // MONO_TYPE_ARRAY
				TargetType eklass = ReadMonoClass (language, target, element_class);
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
