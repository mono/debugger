using System;
using System.Collections;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : TargetClass
	{
		public readonly MonoSymbolFile SymbolFile;
		public readonly TargetAddress KlassAddress;
		public readonly TargetAddress GenericContainer;
		public readonly TargetAddress GenericClass;

		public readonly Cecil.TypeDefinition CecilType;

		MonoClassType type;

		MonoClassInfo parent_info;
		TargetAddress parent_klass = TargetAddress.Null;

		MonoFieldInfo[] fields;
		MonoFieldInfo[] static_fields;
		MonoPropertyInfo[] properties;
		MonoPropertyInfo[] static_properties;
		MonoEventInfo[] events;
		MonoEventInfo[] static_events;
		MonoMethodInfo[] methods;
		MonoMethodInfo[] static_methods;
		MonoMethodInfo[] constructors;
		MonoMethodInfo[] static_constructors;

		TargetType[] field_types;
		int[] field_offsets;

		Hashtable method_hash;

		public static MonoClassInfo ReadClassInfo (MonoLanguageBackend mono,
							   TargetMemoryAccess target,
							   TargetAddress klass)
		{
			TargetAddress image = mono.MonoRuntime.MonoClassGetMonoImage (target, klass);
			MonoSymbolFile file = mono.GetImage (image);
			if (file == null)
				throw new InternalError ();

			int token = mono.MonoRuntime.MonoClassGetToken (target, klass);
			if ((token & 0xff000000) != 0x02000000)
				throw new InternalError ();

			Cecil.TypeDefinition typedef;
			typedef = (Cecil.TypeDefinition) file.ModuleDefinition.LookupByToken (
				Cecil.Metadata.TokenType.TypeDef, token & 0x00ffffff);
			if (typedef == null)
				throw new InternalError ();

			MonoClassInfo info = new MonoClassInfo (file, typedef, target, klass);
			Console.WriteLine ("READ CLASS INFO: {0} {1} {2} {3}",
					   klass, typedef, info.GenericContainer,
					   info.GenericClass);
			info.type = file.LookupMonoClass (typedef);
			return info;
		}

		public static MonoClassInfo ReadClassInfo (MonoSymbolFile file,
							   Cecil.TypeDefinition typedef,
							   TargetMemoryAccess target,
							   TargetAddress klass,
							   out MonoClassType type)
		{
			MonoClassInfo info = new MonoClassInfo (file, typedef, target, klass);
			type = new MonoClassType (file, typedef, info);
			info.type = type;
			return info;
		}

		protected MonoClassInfo (MonoSymbolFile file, Cecil.TypeDefinition typedef,
					 TargetMemoryAccess target, TargetAddress klass)
		{
			this.SymbolFile = file;
			this.KlassAddress = klass;
			this.CecilType = typedef;

			GenericClass = MonoRuntime.MonoClassGetGenericClass (target, klass);
			GenericContainer = MonoRuntime.MonoClassGetGenericContainer (target, klass);
		}

		protected MonoRuntime MonoRuntime {
			get { return SymbolFile.MonoLanguage.MonoRuntime; }
		}

		public override TargetClassType Type {
			get { return type; }
		}

		internal MonoClassType MonoClassType {
			get { return type; }
		}

		public bool IsGenericClass {
			get { return !GenericClass.IsNull; }
		}

		void get_fields ()
		{
			if (fields != null)
				return;

			int num_fields = 0, num_sfields = 0;
			foreach (Cecil.FieldDefinition field in CecilType.Fields) {
				if (field.IsStatic)
					num_sfields++;
				else
					num_fields++;
			}

			fields = new MonoFieldInfo [num_fields];
			static_fields = new MonoFieldInfo [num_sfields];

			int pos = 0, spos = 0, i = 0;
			foreach (Cecil.FieldDefinition field in CecilType.Fields) {
				TargetType ftype = SymbolFile.MonoLanguage.LookupMonoType (field.FieldType);

				if (ftype == null)
					ftype = SymbolFile.MonoLanguage.VoidType;

				if (field.IsStatic) {
					static_fields [spos] = new MonoFieldInfo (
						type, ftype, spos, i, field);
					spos++;
				} else {
					fields [pos] = new MonoFieldInfo (
						type, ftype, pos, i, field);
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

		void get_properties ()
		{
			if (properties != null)
				return;

			int num_sproperties = 0, num_properties = 0;
			foreach (Cecil.PropertyDefinition prop in CecilType.Properties) {
				Cecil.MethodDefinition m = prop.GetMethod;
				if (m == null) m = prop.SetMethod;

				if (m.IsStatic)
					num_sproperties++;
				else
					num_properties++;
			}

			properties = new MonoPropertyInfo [num_properties];
			static_properties = new MonoPropertyInfo [num_sproperties];

			int pos = 0, spos = 0;
			foreach (Cecil.PropertyDefinition prop in CecilType.Properties) {
				Cecil.MethodDefinition m = prop.GetMethod;
				if (m == null) m = prop.SetMethod;

				if (m.IsStatic) {
					static_properties [spos] = MonoPropertyInfo.Create (
						type, spos, prop, true);
					spos++;
				}
				else {
					properties [pos] = MonoPropertyInfo.Create (
						type, pos, prop, false);
					pos++;
				}
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
			foreach (Cecil.EventDefinition ev in CecilType.Events) {
				Cecil.MethodDefinition m = ev.AddMethod;

				if (m.IsStatic)
					num_sevents++;
				else
					num_events++;
			}

			events = new MonoEventInfo [num_events];
			static_events = new MonoEventInfo [num_sevents];

			int pos = 0, spos = 0;
			foreach (Cecil.EventDefinition ev in CecilType.Events) {
				Cecil.MethodDefinition m = ev.AddMethod;

				if (m.IsStatic) {
					static_events [spos] = MonoEventInfo.Create (type, spos, ev, true);
					spos++;
				}
				else {
					events [pos] = MonoEventInfo.Create (type, pos, ev, false);
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

		void get_methods ()
		{
			if (methods != null)
				return;

			int num_methods = 0, num_smethods = 0;
			foreach (Cecil.MethodDefinition method in CecilType.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				if (method.IsStatic)
					num_smethods++;
				else
					num_methods++;
			}

			methods = new MonoMethodInfo [num_methods];
			static_methods = new MonoMethodInfo [num_smethods];

			int pos = 0, spos = 0;
			foreach (Cecil.MethodDefinition method in CecilType.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				if (method.IsStatic) {
					static_methods [spos] = MonoMethodInfo.Create (type, spos, method);
					spos++;
				} else {
					methods [pos] = MonoMethodInfo.Create (type, pos, method);
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

		void get_constructors ()
		{
			if (constructors != null)
				return;

			int num_ctors = 0, num_sctors = 0;
			foreach (Cecil.MethodDefinition method in CecilType.Constructors) {
				if (method.IsStatic)
					num_sctors++;
				else
					num_ctors++;
			}

			constructors = new MonoMethodInfo [num_ctors];
			static_constructors = new MonoMethodInfo [num_sctors];

			int pos = 0, spos = 0;
			foreach (Cecil.MethodDefinition method in CecilType.Constructors) {
				if (method.IsStatic) {
					static_constructors [spos] = MonoMethodInfo.Create (
						type, spos, method);
					spos++;
				} else {
					constructors [pos] = MonoMethodInfo.Create (
						type, pos, method);
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

		void get_field_offsets (TargetMemoryAccess target)
		{
			if (field_offsets != null)
				return;

			int field_count = MonoRuntime.MonoClassGetFieldCount (target, KlassAddress);

			field_offsets = new int [field_count];
			field_types = new TargetType [field_count];

			for (int i = 0; i < field_count; i++) {
				TargetAddress type_addr = MonoRuntime.MonoClassGetFieldType (
					target, KlassAddress, i);

				field_types [i] = SymbolFile.MonoLanguage.ReadType (target, type_addr);
				field_offsets [i] = MonoRuntime.MonoClassGetFieldOffset (
					target, KlassAddress, i);
			}
		}

		public override TargetObject GetField (Thread thread,
						       TargetStructObject instance,
						       TargetFieldInfo field)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					GetField (target, instance, field);
					return null;
			});
		}

		internal TargetObject GetField (TargetMemoryAccess target,
						TargetStructObject instance,
						TargetFieldInfo field)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!Type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			if (field_loc.HasAddress && field_loc.GetAddress (target).IsNull)
				return null;

			return type.GetObject (target, field_loc);
		}

		public override void SetField (Thread thread, TargetStructObject instance,
					       TargetFieldInfo field, TargetObject value)
		{
			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					SetField (target, instance, field, value);
					return null;
			});
		}

		internal void SetField (TargetMemoryAccess target, TargetStructObject instance,
					TargetFieldInfo field, TargetObject obj)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!Type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			type.SetObject (target, field_loc, obj);
		}

		public override TargetObject GetStaticField (Thread thread, TargetFieldInfo field)
		{
			TargetAddress data_address = thread.CallMethod (
				SymbolFile.MonoLanguage.MonoDebuggerInfo.ClassGetStaticFieldData,
				KlassAddress, 0);

			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetStaticField (target, field, data_address);
			});
		}

		internal TargetObject GetStaticField (TargetMemoryAccess target, TargetFieldInfo field,
						      TargetAddress data_address)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			return type.GetObject (target, field_loc);
		}

		public override void SetStaticField (Thread thread, TargetFieldInfo field,
						     TargetObject obj)
		{
			TargetAddress data_address = thread.CallMethod (
				SymbolFile.MonoLanguage.MonoDebuggerInfo.ClassGetStaticFieldData,
				KlassAddress, 0);

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					SetStaticField (target, field, data_address, obj);
					return null;
			});
		}

		internal void SetStaticField (TargetMemoryAccess target, TargetFieldInfo field,
					      TargetAddress data_address, TargetObject obj)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			type.SetObject (target, field_loc, obj);
		}

		void get_methods (TargetMemoryAccess target)
		{
			if (methods != null)
				return;

			method_hash = new Hashtable ();
			int method_count = MonoRuntime.MonoClassGetMethodCount (target, KlassAddress);

			for (int i = 0; i < method_count; i++) {
				TargetAddress address = MonoRuntime.MonoClassGetMethod (
					target, KlassAddress, i);

				int mtoken = MonoRuntime.MonoMethodGetToken (target, address);
				if (mtoken == 0)
					continue;

				method_hash.Add (mtoken, address);
			}
		}

		public TargetAddress GetMethodAddress (TargetMemoryAccess target, int token)
		{
			get_methods (target);
			if (!method_hash.Contains (token))
				throw new InternalError ();
			return (TargetAddress) method_hash [token];
		}

		void get_parent (TargetMemoryAccess target)
		{
			parent_klass = MonoRuntime.MonoClassGetParent (target, KlassAddress);

			parent_info = ReadClassInfo (SymbolFile.MonoLanguage, target, parent_klass);
		}

		public override bool HasParent {
			get { return type.HasParent; }
		}

		public override TargetClass GetParent (Thread thread)
		{
			if (!parent_klass.IsNull)
				return parent_info;

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					get_parent (target);
					return null;
			});
			return parent_info;
		}
	}
}
