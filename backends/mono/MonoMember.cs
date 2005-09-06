using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	[Serializable]
	internal abstract class MonoMember : ITargetMemberInfo
	{
		public readonly MonoSymbolFile File;
		public readonly string Name;
		public readonly int Index;
		public readonly bool IsStatic;

		public MonoMember (MonoSymbolFile file, Cecil.IMemberReference minfo, int index,
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
		public readonly Cecil.IFieldDefinition FieldInfo;
		public readonly int Position;

		public MonoFieldInfo (MonoSymbolFile file, int index, int pos, Cecil.IFieldDefinition finfo)
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
#if FIXME
			if ((FieldInfo != null) && FieldInfo.DeclaringType.IsEnum) {
				object value = FieldInfo.Constant;
			  
				return type.File.MonoLanguage.CreateInstance (target, (int)value);
			} else {
				throw new InvalidOperationException ();
			}
#else
			throw new NotImplementedException ();
#endif
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

		internal MonoMethodInfo (MonoClassType klass, int index, Cecil.IMethodDefinition minfo)
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

		string compute_fullname (Cecil.IMethodDefinition minfo)
		{
			StringBuilder sb = new StringBuilder ();
			bool first = true;
			foreach (Cecil.IParameterReference pinfo in minfo.Parameters) {
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

		internal MonoEventInfo (MonoClassType klass, int index, Cecil.IEventDefinition einfo,
					bool is_static)
			: base (klass.File, einfo, index, is_static)
		{
			Klass = klass;

			type = File.MonoLanguage.LookupMonoType (einfo.EventType);

			Cecil.IMethodDefinition add = einfo.AddMethod;
			AddType = new MonoFunctionType (File, Klass, add);

			Cecil.IMethodDefinition remove = einfo.RemoveMethod;
			RemoveType = new MonoFunctionType (File, Klass, remove);
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

		internal MonoPropertyInfo (MonoClassType klass, int index, Cecil.IPropertyDefinition pinfo,
					   bool is_static)
			: base (klass.File, pinfo, index, is_static)
		{
			Klass = klass;
			type = File.MonoLanguage.LookupMonoType (pinfo.PropertyType);
			CanRead = pinfo.GetMethod != null;
			CanWrite = pinfo.SetMethod != null;

			if (pinfo.GetMethod != null)
				GetterType = new MonoFunctionType (File, Klass, pinfo.GetMethod);

			if (pinfo.SetMethod != null)
				SetterType = new MonoFunctionType (File, Klass, pinfo.SetMethod);
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
