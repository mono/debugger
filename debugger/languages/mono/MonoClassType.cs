using System;
using System.Text;
using System.Collections;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal delegate void ClassInitHandler (TargetMemoryAccess target, TargetAddress klass);

	internal class MonoClassType : TargetClassType
	{
		MonoFieldInfo[] fields;
		MonoMethodInfo[] methods;
		MonoPropertyInfo[] properties;
		MonoEventInfo[] events;
		MonoMethodInfo[] constructors;

		Cecil.TypeDefinition type;
		MonoSymbolFile file;
		MonoClassType parent_type;
		MonoClassInfo class_info;

		Hashtable load_handlers;
		int load_handler_id;

		string full_name;

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

			if (type.GenericParameters.Count > 0) {
				StringBuilder sb = new StringBuilder (type.FullName);
				sb.Append ('<');
				for (int i = 0; i < type.GenericParameters.Count; i++) {
					if (i > 0)
						sb.Append (',');
					sb.Append (type.GenericParameters [i].Name);
				}
				sb.Append ('>');
				full_name = sb.ToString ();
			} else
				full_name = type.FullName;
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
			get { return full_name; }
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

		internal override TargetStructType GetParentType (TargetMemoryAccess target)
		{
			if (parent_type != null)
				return parent_type;

			ResolveClass (target, true);

			MonoClassInfo parent = class_info.GetParent (target);
			if (parent == null)
				return null;

			if (!parent.IsGenericClass)
				return parent.ClassType;

			return File.MonoLanguage.ReadGenericClass (target, parent.GenericClass);
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

			fields = new MonoFieldInfo [type.Fields.Count];

			for (int i = 0; i < fields.Length; i++) {
				Cecil.FieldDefinition field = type.Fields [i];
				TargetType ftype = File.MonoLanguage.LookupMonoType (field.FieldType);
				fields [i] = new MonoFieldInfo (this, ftype, i, field);
			}
		}

		public override TargetFieldInfo[] Fields {
			get {
				get_fields ();
				return fields;
			}
		}

		void get_methods ()
		{
			if (methods != null)
				return;

			int num_methods = 0;
			foreach (Cecil.MethodDefinition method in type.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				num_methods++;
			}

			methods = new MonoMethodInfo [num_methods];

			int pos = 0;
			foreach (Cecil.MethodDefinition method in type.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				methods [pos] = MonoMethodInfo.Create (this, pos, method);
				pos++;
			}
		}

		void get_properties ()
		{
			if (properties != null)
				return;

			properties = new MonoPropertyInfo [type.Properties.Count];

			for (int i = 0; i < properties.Length; i++) {
				Cecil.PropertyDefinition prop = type.Properties [i];
				Cecil.MethodDefinition m = prop.GetMethod;
				if (m == null) m = prop.SetMethod;

				properties [i] = MonoPropertyInfo.Create (this, i, prop);
			}
		}

		public override TargetMethodInfo[] Methods {
			get {
				get_methods ();
				return methods;
			}
		}

		public override TargetPropertyInfo[] Properties {
			get {
				get_properties ();
				return properties;
			}
		}

		void get_events ()
		{
			if (events != null)
				return;

			events = new MonoEventInfo [type.Events.Count];

			for (int i = 0; i < events.Length; i++) {
				Cecil.EventDefinition ev = type.Events [i];
				events [i] = MonoEventInfo.Create (this, i, ev);
			}
		}

		public override TargetEventInfo[] Events {
			get {
				get_events ();
				return events;
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

			constructors = new MonoMethodInfo [type.Constructors.Count];

			for (int i = 0; i < constructors.Length; i++) {
				Cecil.MethodDefinition method = type.Constructors [i];
				constructors [i] = MonoMethodInfo.Create (this, i, method);
			}
		}

		public override TargetMethodInfo[] Constructors {
			get {
				get_constructors ();
				return constructors;
			}
		}

		internal override TargetClass GetClass (TargetMemoryAccess target)
		{
			return ResolveClass (target, false);
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

		internal MonoClassObject GetCurrentObject (TargetMemoryAccess target,
							   TargetLocation location)
		{
			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = target.ReadAddress (location.GetAddress (target));
			address = target.ReadAddress (address);

			TargetType current = File.MonoLanguage.ReadMonoClass (target, address);
			if (current == null)
				return null;

			if (IsByRef && !current.IsByRef) // Unbox
				location = location.GetLocationAtOffset (
					2 * target.TargetMemoryInfo.TargetAddressSize);

			return (MonoClassObject) current.GetObject (target, location);
		}
	}
}
