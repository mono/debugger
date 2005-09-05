using System;
using System.Text;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	[Serializable]
	internal abstract class MonoMember : ITargetMemberInfo
	{
		public readonly MonoSymbolFile File;
		public readonly string Name;
		public readonly int Index;
		public readonly int Position;
		public readonly bool IsStatic;

		public MonoMember (MonoSymbolFile file, R.MemberInfo minfo, int index, int pos,
				   bool is_static)
		{
			this.File = file;
			this.Index = index;
			this.Name = minfo.Name;
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
			get { return Name; }
		}

		int ITargetMemberInfo.Index {
			get { return Index; }
		}

		bool ITargetMemberInfo.IsStatic {
			get { return IsStatic; }
		}

		protected abstract string MyToString ();

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4})",
					      GetType (), Type, Index, IsStatic, MyToString ());
		}
	}

	[Serializable]
	internal class MonoFieldInfo : MonoMember, ITargetFieldInfo
	{
		MonoType type;
		bool is_literal;

		[NonSerialized]
		public readonly R.FieldInfo FieldInfo;

		public MonoFieldInfo (MonoSymbolFile file, int index, int pos, R.FieldInfo finfo)
			: base (file, finfo, index, pos, finfo.IsStatic)
		{
			FieldInfo = finfo;
			is_literal = finfo.IsLiteral;
			type = File.MonoLanguage.LookupMonoType (finfo.FieldType);
		}

		public override MonoType Type {
			get { return type; }
		}

		int ITargetFieldInfo.Offset {
			get { return 0; }
		}

		public bool HasConstValue {
			get { return is_literal; }
		}

		public ITargetObject GetConstValue (ITargetAccess target) 
		{
			// this is definitely swayed toward enums,
			// where we know we can get the const value
			// from a null instance (i.e. it's a static
			// field.  we need to take into account
			// finfo.IsStatic, though.
			if ((FieldInfo != null) && FieldInfo.DeclaringType.IsEnum) {
				object value = FieldInfo.GetValue (null);
			  
				return type.File.MonoLanguage.CreateInstance (target, (int)value);
			} else {
				throw new InvalidOperationException ();
			}
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}", type);
		}
	}

	[Serializable]
	internal class MonoMethodInfo : MonoMember, ITargetMethodInfo
	{
		public readonly MonoFunctionType FunctionType;
		public readonly MonoClassType Klass;
		public readonly string FullName;

		internal MonoMethodInfo (MonoClassType klass, int index, R.MethodBase minfo)
			: base (klass.File, minfo, index, MonoDebuggerSupport.GetMethodIndex (minfo),
				minfo.IsStatic)
		{
			Klass = klass;
			FunctionType = new MonoFunctionType (File, Klass, minfo, Position - 1);
			FullName = compute_fullname (minfo);
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
				return FullName;
			}
		}

		string compute_fullname (R.MethodBase minfo)
		{
			StringBuilder sb = new StringBuilder ();
			bool first = true;
			foreach (R.ParameterInfo pinfo in minfo.GetParameters ()) {
				if (first)
					first = false;
				else
					sb.Append (",");
				sb.Append (pinfo.ParameterType);
			}

			return String.Format ("{0}({1})", Name, sb.ToString ());
		}

		internal ITargetFunctionObject Get (StackFrame frame)
		{
			MonoFunctionTypeInfo func = FunctionType.GetTypeInfo () as MonoFunctionTypeInfo;
			if (func == null)
				return null;

			return func.GetStaticObject (frame);
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}", FunctionType);
		}
	}

	[Serializable]
	internal class MonoEventInfo : MonoMember, ITargetEventInfo
	{
		MonoType type;
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType AddType, RemoveType;

		internal MonoEventInfo (MonoClassType klass, int index, R.EventInfo einfo,
					bool is_static)
			: base (klass.File, einfo, index, index, is_static)
		{
			int pos;

			Klass = klass;

			type = File.MonoLanguage.LookupMonoType (einfo.EventHandlerType);

			R.MethodInfo add = einfo.GetAddMethod ();
			pos = MonoDebuggerSupport.GetMethodIndex (add);
			AddType = new MonoFunctionType (File, Klass, add, pos - 1);

			R.MethodInfo remove = einfo.GetRemoveMethod ();
			pos = MonoDebuggerSupport.GetMethodIndex (remove);
			RemoveType = new MonoFunctionType (File, Klass, remove, pos - 1);
		}

		public override MonoType Type {
			get { return type; }
		}

		ITargetFunctionType ITargetEventInfo.Add {
			get {
				return AddType;
			}
		}

		ITargetFunctionType ITargetEventInfo.Remove {
			get {
				return RemoveType;
			}
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}", AddType, RemoveType);
		}
	}

	[Serializable]
	internal class MonoPropertyInfo : MonoMember, ITargetPropertyInfo
	{
		MonoType type;
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType GetterType, SetterType;
		public readonly bool CanRead, CanWrite;

		internal MonoPropertyInfo (MonoClassType klass, int index, R.PropertyInfo pinfo,
					   bool is_static)
			: base (klass.File, pinfo, index, index, is_static)
		{
			Klass = klass;
			type = File.MonoLanguage.LookupMonoType (pinfo.PropertyType);
			CanRead = pinfo.CanRead;
			CanWrite = pinfo.CanWrite;

			if (CanRead) {
				R.MethodInfo getter = pinfo.GetGetMethod (true);
				int pos = MonoDebuggerSupport.GetMethodIndex (getter);
				GetterType = new MonoFunctionType (File, Klass, getter, pos - 1);
			}

			if (CanWrite) {
				R.MethodInfo setter = pinfo.GetSetMethod (true);
				int pos = MonoDebuggerSupport.GetMethodIndex (setter);
				SetterType = new MonoFunctionType (File, Klass, setter, pos - 1);
			}
		}

		public override MonoType Type {
			get { return type; }
		}

		bool ITargetPropertyInfo.CanRead {
			get { return CanRead; }
		}

		ITargetFunctionType ITargetPropertyInfo.Getter {
			get {
				if (!CanRead)
					throw new InvalidOperationException ();

				return GetterType;
			}
		}

		bool ITargetPropertyInfo.CanWrite {
			get {
				return CanWrite;
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
			if (!CanRead)
				throw new InvalidOperationException ();

			MonoFunctionTypeInfo getter = GetterType.GetTypeInfo () as MonoFunctionTypeInfo;
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
			if (!CanRead)
				throw new InvalidOperationException ();

			MonoFunctionTypeInfo getter = GetterType.GetTypeInfo () as MonoFunctionTypeInfo;
			if (getter == null)
				return null;

			return getter.InvokeStatic (frame, new MonoObject [0], false);
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}", CanRead, CanWrite);
		}
	}
}
