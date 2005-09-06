using System;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

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

		public MonoClassType (MonoSymbolFile file, Cecil.ITypeDefinition type)
			: base (file, TargetObjectKind.Class, type)
		{
			this.type = type;

			if (type.BaseType != null)
				parent_type = file.MonoLanguage.LookupMonoType (type.BaseType) as MonoClassType;
		}

		public override bool IsByRef {
			get { return !Type.IsValueType; }
		}

		public bool HasParent {
			get { return parent_type != null; }
		}

		public ITargetClassType ParentType {
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
			MonoClassInfo info = GetTypeInfo () as MonoClassInfo;
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

		public MonoMethodInfo GetMethod (int index)
		{
			get_methods ();
			if (index < first_method)
				return parent_type.GetMethod (index);

			return methods [index - first_method];
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

		public ITargetFunctionObject GetStaticMethod (StackFrame frame, int index)
		{
			get_methods ();

			if (index < first_smethod)
				return parent_type.GetStaticMethod (frame, index);

			try {
				MonoFunctionType ftype = static_methods [index - first_smethod].FunctionType;
				MonoFunctionTypeInfo finfo = ftype.GetTypeInfo () as MonoFunctionTypeInfo;
				if (finfo == null)
					return null;

				return finfo.GetStaticObject (frame);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
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
					static_properties [pos] = new MonoPropertyInfo (this, spos, prop, false);
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

		public ITargetObject GetStaticProperty (StackFrame frame, int index)
		{
			get_properties ();
			return static_properties [index].Get (frame);
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

			constructors = new MonoMethodInfo [num_methods];
			static_constructors = new MonoMethodInfo [num_smethods];

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

		public ITargetFunctionObject GetConstructor (StackFrame frame, int index)
		{
			get_constructors ();
 			return constructors [index].Get (frame);
		}

		public ITargetMethodInfo[] StaticConstructors {
			get {
				get_constructors ();
				return static_constructors;
			}
		}

		public ITargetFunctionObject GetStaticConstructor (StackFrame frame, int index)
		{
			get_constructors ();
 			return static_constructors [index].Get (frame);
		}

		protected override MonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			return new MonoClassInfo (this, info);
		}

	}
}
