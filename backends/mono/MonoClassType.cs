using System;
using System.Text;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

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

		public ITargetClassType ParentType {
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

		public ITargetFunctionObject GetStaticMethod (StackFrame frame, int index)
		{
			get_methods ();

			if (index < first_smethod)
				return parent_type.GetStaticMethod (frame, index);

			try {
				MonoFunctionType ftype = static_methods [index - first_smethod].FunctionType;

				return ftype.GetStaticObject (frame);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
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

		public ITargetObject GetStaticProperty (StackFrame frame, int index)
		{
			get_properties ();
			return static_properties [index].Get (frame);
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

		protected override IMonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			return new MonoClassInfo (this, info);
		}

	}
}
