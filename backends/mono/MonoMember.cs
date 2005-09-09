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
		public readonly bool IsStatic;

		public MonoMember (MonoSymbolFile file, R.MemberInfo minfo, int index,
				   bool is_static)
		{
			this.File = file;
			this.Index = index;
			this.Name = minfo.Name;
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
		public readonly int Position;

		public MonoFieldInfo (MonoSymbolFile file, int index, int pos, R.FieldInfo finfo)
			: base (file, finfo, index, finfo.IsStatic)
		{
			FieldInfo = finfo;
			Position = pos;
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
			: base (klass.File, minfo, index, minfo.IsStatic)
		{
			Klass = klass;
			FunctionType = new MonoFunctionType (File, Klass, minfo);
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

		protected override string MyToString ()
		{
			return String.Format ("{0}", FullName);
		}
	}

	[Serializable]
	internal class MonoEventInfo : MonoMember, ITargetEventInfo
	{
		MonoType type;
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType AddType, RemoveType, RaiseType;

		internal MonoEventInfo (MonoClassType klass, int index, R.EventInfo einfo,
					bool is_static)
			: base (klass.File, einfo, index, is_static)
		{
			Klass = klass;

			type = File.MonoLanguage.LookupMonoType (einfo.EventHandlerType);

			R.MethodInfo add = einfo.GetAddMethod ();
			if (add != null)
				AddType = new MonoFunctionType (File, Klass, add);

			R.MethodInfo remove = einfo.GetRemoveMethod ();
			if (remove != null)
				RemoveType = new MonoFunctionType (File, Klass, remove);

			R.MethodInfo raise = einfo.GetRaiseMethod (true);
			if (raise != null)
				RaiseType = new MonoFunctionType (File, Klass, raise);
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

		ITargetFunctionType ITargetEventInfo.Raise {
			get {
				return RaiseType;
			}
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}:{2}", AddType, RemoveType, RaiseType);
		}
	}

	[Serializable]
	internal class MonoPropertyInfo : MonoMember, ITargetPropertyInfo
	{
		MonoType type;
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType Getter, Setter;
		public readonly bool CanRead, CanWrite;

		internal MonoPropertyInfo (MonoClassType klass, int index, R.PropertyInfo pinfo,
					   bool is_static)
			: base (klass.File, pinfo, index, is_static)
		{
			Klass = klass;
			type = File.MonoLanguage.LookupMonoType (pinfo.PropertyType);
			CanRead = pinfo.CanRead;
			CanWrite = pinfo.CanWrite;

			if (CanRead) {
				R.MethodInfo getter = pinfo.GetGetMethod (true);
				Getter = new MonoFunctionType (File, Klass, getter);
			}

			if (CanWrite) {
				R.MethodInfo setter = pinfo.GetSetMethod (true);
				Setter = new MonoFunctionType (File, Klass, setter);
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

				return Getter;
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

				return Setter;
			}
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}", CanRead, CanWrite);
		}
	}
}
