using System;
using System.Text;
using System.Collections;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

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

		MonoClassType parent_type;

		public MonoClassType (MonoSymbolFile file, Type type)
			: base (file, TargetObjectKind.Class, type)
		{
			if (type.BaseType != null)
				parent_type = file.MonoLanguage.LookupMonoType (type.BaseType) as MonoClassType;
		}

		public override bool IsByRef {
			get { return !Type.IsValueType; }
		}

		public override bool HasFixedSize {
			get { return true; }
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

			R.FieldInfo[] finfo = type.GetFields (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			foreach (R.FieldInfo field in finfo) {
				if (field.IsStatic)
					num_sfields++;
				else
					num_fields++;
			}

			fields = new MonoFieldInfo [num_fields];
			static_fields = new MonoFieldInfo [num_sfields];

			int pos = 0, spos = 0;
			for (int i = 0; i < finfo.Length; i++) {
				if (finfo [i].IsStatic) {
					static_fields [spos] = new MonoFieldInfo (File, spos, i, finfo [i]);
					spos++;
				} else {
					fields [pos] = new MonoFieldInfo (File, pos, i, finfo [i]);
					pos++;
				}
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

		void get_methods ()
		{
			if (methods != null)
				return;

			R.MethodInfo[] minfo = type.GetMethods (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			foreach (R.MethodInfo method in minfo) {
				if ((method.Attributes & R.MethodAttributes.SpecialName) != 0)
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
			for (int i = 0; i < minfo.Length; i++) {
				if ((minfo [i].Attributes & R.MethodAttributes.SpecialName) != 0)
					continue;
				if (minfo [i].IsStatic) {
					static_methods [spos] = new MonoMethodInfo (this, first_smethod + spos, minfo [i]);
					spos++;
				} else {
					methods [pos] = new MonoMethodInfo (this, first_method + pos, minfo [i]);
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

		protected ITargetFunctionObject CreateFunctionObject (ITargetAccess target,
								      MonoFunctionType ftype)
		{
			try {
				MonoClassInfo info = GetTypeInfo () as MonoClassInfo;
				if (info == null)
					return null;

				TargetAddress address = info.GetMethodAddress (target, ftype.Token);
				return ftype.GetObject (new AbsoluteTargetLocation (target, address));
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetFunctionObject GetMethod (ITargetAccess target, int index)
		{
			get_methods ();

			if (index < first_method)
				return parent_type.GetMethod (target, index);

			return CreateFunctionObject (
				target, methods [index - first_method].FunctionType);
		}

		public ITargetFunctionObject GetStaticMethod (ITargetAccess target, int index)
		{
			get_methods ();

			if (index < first_smethod)
				return parent_type.GetStaticMethod (target, index);

			return CreateFunctionObject (
				target, static_methods [index - first_smethod].FunctionType);
		}

		void get_properties ()
		{
			if (properties != null)
				return;

			R.PropertyInfo[] pinfo = type.GetProperties (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			properties = new MonoPropertyInfo [pinfo.Length];

			for (int i = 0; i < pinfo.Length; i++)
				properties [i] = new MonoPropertyInfo (this, i, pinfo [i], false);

			pinfo = type.GetProperties (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			static_properties = new MonoPropertyInfo [pinfo.Length];

			for (int i = 0; i < pinfo.Length; i++)
				static_properties [i] = new MonoPropertyInfo (this, i, pinfo [i], true);
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

		internal ITargetObject GetProperty (MonoClassObject instance, int index)
		{
			try {
				get_properties ();
				ITargetAccess target = instance.Location.TargetAccess;
				ITargetFunctionObject func = CreateFunctionObject (
					target, properties [index].Getter);
				return func.Invoke (target, instance, new ITargetObject [0], false);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetObject GetStaticProperty (ITargetAccess target, int index)
		{
			try {
				get_properties ();
				ITargetFunctionObject func = CreateFunctionObject (
					target, static_properties [index].Getter);
				return func.Invoke (target, null, new ITargetObject [0], false);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		void get_events ()
		{
			if (events != null)
				return;

			R.EventInfo[] einfo = type.GetEvents (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			events = new MonoEventInfo [einfo.Length];

			for (int i = 0; i < einfo.Length; i++)
				events [i] = new MonoEventInfo (this, i, einfo [i], false);

			einfo = type.GetEvents (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			static_events = new MonoEventInfo [einfo.Length];

			for (int i = 0; i < einfo.Length; i++)
				static_events [i] = new MonoEventInfo (this, i, einfo [i], true);
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

			R.ConstructorInfo[] minfo = type.GetConstructors (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			foreach (R.ConstructorInfo method in minfo) {
				if (method.IsStatic)
					num_sctors++;
				else
					num_ctors++;
			}

			constructors = new MonoMethodInfo [num_ctors];
			static_constructors = new MonoMethodInfo [num_sctors];

			int pos = 0, spos = 0;
			for (int i = 0; i < minfo.Length; i++) {
				if (minfo [i].IsStatic) {
					static_constructors [spos] = new MonoMethodInfo (this, spos, minfo [i]);
					spos++;
				} else {
					constructors [pos] = new MonoMethodInfo (this, pos, minfo [i]);
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

		public ITargetFunctionObject GetConstructor (ITargetAccess target, int index)
		{
			get_constructors ();
 			return CreateFunctionObject (
				target, constructors [index].FunctionType);
		}

		public ITargetFunctionObject GetStaticConstructor (ITargetAccess target, int index)
		{
			get_constructors ();
 			return CreateFunctionObject (
				target, static_constructors [index].FunctionType);
		}

		protected override IMonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			return new MonoClassInfo (this, info);
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
	}
}
