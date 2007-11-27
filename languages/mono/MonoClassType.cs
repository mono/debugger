using System;
using System.Text;
using System.Collections;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal delegate void ClassInitHandler (TargetMemoryAccess target, TargetAddress klass);

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
		int num_fields = 0, num_sfields = 0;

		Cecil.TypeDefinition type;
		MonoSymbolFile file;
		MonoClassType parent_type;
		MonoClassInfo class_info;

		Hashtable load_handlers;
		int load_handler_id;

		public MonoClassType (MonoSymbolFile file, Cecil.TypeDefinition type)
			: base (file.MonoLanguage, TargetObjectKind.Class)
		{
			this.type = type;
			this.file = file;

			if (type.BaseType != null) {
				TargetType parent = file.MonoLanguage.LookupMonoType (type.BaseType);
				if (parent != null)
					parent_type = (MonoClassType) parent.ClassType;
			}
		}

		public MonoClassType (MonoSymbolFile file, Cecil.TypeDefinition typedef,
				      MonoClassInfo class_info)
			: this (file, typedef)
		{
			this.class_info = class_info;
		}

		public Cecil.TypeDefinition Type {
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

		public override Module Module {
			get { return file.Module; }
		}

		public override bool HasClassType {
			get { return true; }
		}

		public override TargetClassType ClassType {
			get { return this; }
		}

		internal int Token {
			get { return (int) (type.MetadataToken.TokenType + type.MetadataToken.RID); }
		}

		void get_fields ()
		{
			if (fields != null)
				return;

			foreach (Cecil.FieldDefinition field in type.Fields) {
				if (field.IsStatic)
					num_sfields++;
				else
					num_fields++;
			}

			fields = new MonoFieldInfo [num_fields];
			static_fields = new MonoFieldInfo [num_sfields];

			int pos = 0, spos = 0, i = 0;
			foreach (Cecil.FieldDefinition field in type.Fields) {
				TargetType ftype = File.MonoLanguage.LookupMonoType (field.FieldType);
				if (field.IsStatic) {
					static_fields [spos] = new MonoFieldInfo (
						this, ftype, spos, i, field);
					spos++;
				} else {
					fields [pos] = new MonoFieldInfo (
						this, ftype, pos, i, field);
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

			foreach (Cecil.MethodDefinition method in type.Methods) {
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
			foreach (Cecil.MethodDefinition method in type.Methods) {
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

			foreach (Cecil.PropertyDefinition prop in type.Properties) {
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
			foreach (Cecil.PropertyDefinition prop in type.Properties) {
				Cecil.MethodDefinition m = prop.GetMethod;
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
			foreach (Cecil.EventDefinition ev in type.Events) {
				Cecil.MethodDefinition m = ev.AddMethod;

				if (m.IsStatic)
					num_sevents++;
				else
					num_events++;
			}

			events = new MonoEventInfo [num_events];
			static_events = new MonoEventInfo [num_sevents];

			int pos = 0, spos = 0;
			foreach (Cecil.EventDefinition ev in type.Events) {
				Cecil.MethodDefinition m = ev.AddMethod;

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

			foreach (Cecil.MethodDefinition method in type.Constructors) {
				if (method.IsStatic)
					num_sctors++;
				else
					num_ctors++;
			}

			constructors = new MonoMethodInfo [num_ctors];
			static_constructors = new MonoMethodInfo [num_sctors];

			int pos = 0, spos = 0;
			foreach (Cecil.MethodDefinition method in type.Constructors) {
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

		internal MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail)
		{
			if (class_info != null)
				return class_info;

			if (parent_type != null) {
				if (parent_type.ResolveClass (target, fail) == null)
					return null;
			}

			class_info = file.LookupClassInfo (target, (int) type.MetadataToken.ToUInt ());
			if (class_info != null)
				return class_info;

			if (fail)
				throw new TargetException (TargetError.ClassNotInitialized,
							   "Class `{0}' not initialized yet.", Name);

			return null;
		}

		internal MonoClassInfo ClassResolved (TargetMemoryAccess target, TargetAddress klass)
		{
			class_info = File.MonoLanguage.ReadClassInfo (target, klass);
			return class_info;
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			ResolveClass (target, true);
			return new MonoClassObject (this, class_info, location);
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

		public override TargetObject GetStaticField (Thread thread, TargetFieldInfo field)
		{
			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object data) {
					ResolveClass (target, true);
					return null;
			}, null);
			return class_info.GetStaticField (thread, field);
		}

		public override void SetStaticField (Thread thread, TargetFieldInfo field,
						     TargetObject obj)
		{
			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object data) {
					ResolveClass (target, true);
					return null;
			}, null);
			class_info.SetStaticField (thread, field, obj);
		}

		internal MonoClassObject GetCurrentObject (TargetMemoryAccess target,
							   TargetLocation location)
		{
			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = target.ReadAddress (location.GetAddress (target));
			address = target.ReadAddress (address);

			TargetType current = MonoRuntime.ReadMonoClass (File.MonoLanguage, target, address);
			if (current == null)
				return null;

			if (IsByRef && !current.IsByRef) // Unbox
				location = location.GetLocationAtOffset (
					2 * target.TargetMemoryInfo.TargetAddressSize);

			return (MonoClassObject) current.GetObject (target, location);
		}

		internal MonoClassInfo HardResolveClass (Thread thread)
		{
			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object data) {
					ResolveClass (target, false);
					return null;
			}, null);

			if (class_info != null)
				return class_info;

			TargetAddress klass_address = thread.CallMethod (
				file.MonoLanguage.MonoDebuggerInfo.LookupClass,
				file.MonoImage, 0, 0, Name);

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object data) {
					class_info = file.MonoLanguage.ReadClassInfo (
						target, klass_address);
					return null;
			}, null);

			if (class_info == null)
				throw new InternalError ();

			return class_info;
		}
	}
}
