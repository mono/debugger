using System;
using System.Text;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoClass : MonoType, ITargetClassType
	{
		MonoFieldInfo[] fields;
		MonoFieldInfo[] static_fields;
		MonoMethodInfo[] methods;
		MonoMethodInfo[] static_methods;
		MonoPropertyInfo[] properties;
		MonoPropertyInfo[] static_properties;
		MonoClass parent;

		protected MonoClass (MonoSymbolFile file, TargetObjectKind kind, Type type)
			: base (file, kind, type)
		{
			if (type.BaseType != null)
				parent = file.MonoLanguage.LookupType (type.BaseType) as MonoClass;
		}

		public bool HasParent {
			get { return parent != null; }
		}

		public ITargetClassType ParentType {
			get { return parent; }
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
					static_fields [spos] = new MonoFieldInfo (this, spos, i, finfo [i]);
					spos++;
				} else {
					fields [pos] = new MonoFieldInfo (this, pos, i, finfo [i]);
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

		public ITargetObject GetStaticField (StackFrame frame, int index)
		{
			MonoClassInfo info = (MonoClassInfo) Resolve ();
			if (info == null)
				return null;

			return info.GetStaticField (frame, index);
		}

		void get_methods ()
		{
			if (methods != null)
				return;

			int num_methods = 0, num_smethods = 0;

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

			int pos = 0, spos = 0;
			for (int i = 0; i < minfo.Length; i++) {
				if ((minfo [i].Attributes & R.MethodAttributes.SpecialName) != 0)
					continue;
				if (minfo [i].IsStatic) {
					static_methods [spos] = new MonoMethodInfo (this, spos, minfo [i]);
					spos++;
				} else {
					methods [pos] = new MonoMethodInfo (this, pos, minfo [i]);
					pos++;
				}
			}
		}

		internal MonoMethodInfo[] Methods {
			get {
				get_methods ();
				return methods;
			}
		}

		internal MonoMethodInfo[] StaticMethods {
			get {
				get_methods ();
				return static_methods;
			}
		}

		ITargetMethodInfo[] ITargetStructType.Methods {
			get { return Methods; }
		}

		ITargetMethodInfo[] ITargetStructType.StaticMethods {
			get { return StaticMethods; }
		}

		public ITargetFunctionObject GetStaticMethod (StackFrame frame, int index)
		{
			get_methods ();

			try {
				MonoFunctionType ftype = static_methods [index].FunctionType;
				MonoFunctionTypeInfo finfo = (MonoFunctionTypeInfo) ftype.Resolve ();
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

		internal abstract class MonoStructMember : ITargetMemberInfo
		{
			public readonly MonoClass Klass;
			public readonly R.MemberInfo MemberInfo;
			public readonly int Index;
			public readonly int Position;
			public readonly bool IsStatic;

			public MonoStructMember (MonoClass klass, R.MemberInfo minfo, int index, int pos,
						 bool is_static)
			{
				this.Klass = klass;
				this.MemberInfo = minfo;
				this.Index = index;
				this.Position = pos;
				this.IsStatic = is_static;
			}

			public abstract MonoType Type {
				get;
			}

			ITargetType ITargetMemberInfo.Type {
				get { return Type; }
			}

			string ITargetMemberInfo.Name {
				get { return MemberInfo.Name; }
			}

			int ITargetMemberInfo.Index {
				get { return Index; }
			}

			bool ITargetMemberInfo.IsStatic {
				get { return IsStatic; }
			}

			object ITargetMemberInfo.Handle {
				get { return MemberInfo; }
			}

			protected abstract string MyToString ();

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})",
						      GetType (), Klass, Type, Index, IsStatic, MyToString ());
			}
		}

		internal class MonoFieldInfo : MonoStructMember, ITargetFieldInfo
		{
			MonoType type;

			public readonly R.FieldInfo FieldInfo;

			internal MonoFieldInfo (MonoClass klass, int index, int pos, R.FieldInfo finfo)
				: base (klass, finfo, index, pos, finfo.IsStatic)
			{
				FieldInfo = finfo;
				type = klass.File.MonoLanguage.LookupType (finfo.FieldType);
			}

			public override MonoType Type {
				get { return type; }
			}

			int ITargetFieldInfo.Offset {
				get { return 0; }
			}

			protected override string MyToString ()
			{
				return String.Format ("{0}", type);
			}
		}

		internal class MonoMethodInfo : MonoStructMember, ITargetMethodInfo
		{
			public readonly R.MethodBase MethodInfo;
			public readonly MonoFunctionType FunctionType;

			internal MonoMethodInfo (MonoClass klass, int index, R.MethodBase minfo)
				: base (klass, minfo, index, C.MonoDebuggerSupport.GetMethodIndex (minfo),
					minfo.IsStatic)
			{
				MethodInfo = minfo;
				FunctionType = new MonoFunctionType (klass.File, Klass, minfo, Position - 1);
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

		internal class MonoPropertyInfo : MonoStructMember, ITargetPropertyInfo
		{
			MonoType type;
			public readonly R.PropertyInfo PropertyInfo;
			public readonly MonoFunctionType GetterType, SetterType;

			internal MonoPropertyInfo (MonoClass klass, int index, R.PropertyInfo pinfo,
						   bool is_static)
				: base (klass, pinfo, index, index, is_static)
			{
				PropertyInfo = pinfo;
				type = klass.File.MonoLanguage.LookupType (pinfo.PropertyType);

				if (PropertyInfo.CanRead) {
					R.MethodInfo getter = PropertyInfo.GetGetMethod (true);
					int pos = C.MonoDebuggerSupport.GetMethodIndex (getter);
					GetterType = new MonoFunctionType (klass.File, Klass, getter, pos - 1);
				}

				if (PropertyInfo.CanWrite) {
					R.MethodInfo setter = PropertyInfo.GetSetMethod (true);
					int pos = C.MonoDebuggerSupport.GetMethodIndex (setter);
					SetterType = new MonoFunctionType (klass.File, Klass, setter, pos - 1);
				}
			}

			public override MonoType Type {
				get { return type; }
			}

			public bool CanRead {
				get { return PropertyInfo.CanRead; }
			}

			ITargetFunctionType ITargetPropertyInfo.Getter {
				get {
					if (!CanRead)
						throw new InvalidOperationException ();

					return GetterType;
				}
			}

			public bool CanWrite {
				get {
					return PropertyInfo.CanWrite;
				}
			}

			ITargetFunctionType ITargetPropertyInfo.Setter {
				get {
					if (!CanWrite)
						throw new InvalidOperationException ();

					return SetterType;
				}
			}

			internal ITargetObject Get (TargetLocation location)
			{
				if (!PropertyInfo.CanRead)
					throw new InvalidOperationException ();

				MonoFunctionTypeInfo getter = (MonoFunctionTypeInfo) GetterType.Resolve ();
				if (getter == null)
					return null;

				ITargetFunctionObject func = getter.GetObject (location) as ITargetFunctionObject;
				if (func == null)
					return null;

				ITargetObject retval = func.Invoke (new MonoObject [0], false);
				return retval;
			}

			internal ITargetObject Get (StackFrame frame)
			{
				if (!PropertyInfo.CanRead)
					throw new InvalidOperationException ();

				MonoFunctionTypeInfo getter = (MonoFunctionTypeInfo) GetterType.Resolve ();
				if (getter == null)
					return null;

				return getter.InvokeStatic (frame, new MonoObject [0], false);
			}

			protected override string MyToString ()
			{
				return String.Format ("{0}:{1}", CanRead, CanWrite);
			}
		}

		public ITargetEventInfo[] Events {
			get { return new ITargetEventInfo [0]; }
		}

		public ITargetEventInfo[] StaticEvents {
			get { return new ITargetEventInfo [0]; }
		}

		public ITargetObject GetStaticEvent (StackFrame frame, int index)
		{
			return null;
		}

		public ITargetMethodInfo[] Constructors {
			get { return new ITargetMethodInfo [0]; }
		}

		public ITargetFunctionObject GetConstructor (StackFrame frame, int index)
		{
			return null;
		}

		public ITargetMethodInfo[] StaticConstructors {
			get { return new ITargetMethodInfo [0]; }
		}

		public ITargetFunctionObject GetStaticConstructor (StackFrame frame, int index)
		{
			return null;
		}

		protected override MonoTypeInfo DoResolve (TargetBinaryReader info)
		{
			return new MonoClassInfo (this, info);
		}
	}
}
