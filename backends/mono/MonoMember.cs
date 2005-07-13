using System;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoMember : ITargetMemberInfo
	{
		public readonly MonoSymbolFile File;
		public readonly Cecil.IMemberReference MemberInfo;
		public readonly int Index;
		public readonly int Position;
		public readonly bool IsStatic;

		public MonoMember (MonoSymbolFile file, Cecil.IMemberReference minfo, int index, int pos,
				   bool is_static)
		{
			this.File = file;
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

		protected abstract string MyToString ();

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4})",
					      GetType (), Type, Index, IsStatic, MyToString ());
		}
	}

	internal class MonoFieldInfo : MonoMember, ITargetFieldInfo
	{
		MonoType type;

		public readonly Cecil.IFieldDefinition FieldInfo;

		public MonoFieldInfo (MonoSymbolFile file, int index, int pos, Cecil.IFieldDefinition finfo)
			: base (file, finfo, index, pos, finfo.IsStatic)
		{
			FieldInfo = finfo;
			type = File.MonoLanguage.LookupMonoType (finfo.FieldType);
		}

		public override MonoType Type {
			get { return type; }
		}

		int ITargetFieldInfo.Offset {
			get { return 0; }
		}

		public bool HasConstValue {
			get { return FieldInfo.IsLiteral; }
		}

		public ITargetObject GetConstValue (StackFrame frame) 
		{
			// this is definitely swayed toward enums,
			// where we know we can get the const value
			// from a null instance (i.e. it's a static
			// field.  we need to take into account
			// finfo.IsStatic, though.
			if (FieldInfo.DeclaringType.IsEnum) {
				object value = FieldInfo.Constant;
			  
				return type.File.MonoLanguage.CreateInstance (frame, (int)value);
			}
			else {
				// XXX
				return null;
			}
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}", type);
		}
	}

	internal class MonoMethodInfo : MonoMember, ITargetMethodInfo
	{
		public readonly Cecil.IMethodDefinition MethodInfo;
		public readonly MonoFunctionType FunctionType;
		public readonly MonoClassType Klass;

		internal MonoMethodInfo (MonoClassType klass, int index, Cecil.IMethodDefinition minfo)
			: base (klass.File, minfo, index, C.MonoDebuggerSupport.GetMethodIndex (minfo),
				minfo.IsStatic)
		{
			Klass = klass;
			MethodInfo = minfo;
			FunctionType = new MonoFunctionType (File, Klass, minfo, Position - 1);
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
				foreach (Cecil.IParameterDefinition pinfo in MethodInfo.Parameters) {
					if (first)
						first = false;
					else
						sb.Append (",");
					sb.Append (pinfo.ParameterType);
				}

				return String.Format ("{0}({1})", MethodInfo.Name, sb.ToString ());
			}
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

	internal class MonoEventInfo : MonoMember, ITargetEventInfo
	{
		MonoType type;
		public readonly MonoClassType Klass;
		public readonly Cecil.IEventDefinition EventInfo;
		public readonly MonoFunctionType AddType, RemoveType;

		internal MonoEventInfo (MonoClassType klass, int index, Cecil.IEventDefinition einfo,
					bool is_static)
			: base (klass.File, einfo, index, index, is_static)
		{
			int pos;

			Klass = klass;

			EventInfo = einfo;
			type = File.MonoLanguage.LookupMonoType (einfo.EventType);

			Cecil.IMethodDefinition add = EventInfo.AddMethod;
			pos = C.MonoDebuggerSupport.GetMethodIndex (add);
			AddType = new MonoFunctionType (File, Klass, add, pos - 1);

			Cecil.IMethodDefinition remove = EventInfo.RemoveMethod;
			pos = C.MonoDebuggerSupport.GetMethodIndex (remove);
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

	internal class MonoPropertyInfo : MonoMember, ITargetPropertyInfo
	{
		MonoType type;
		public readonly MonoClassType Klass;
		public readonly Cecil.IPropertyDefinition PropertyInfo;
		public readonly MonoFunctionType GetterType, SetterType;

		internal MonoPropertyInfo (MonoClassType klass, int index, Cecil.IPropertyDefinition pinfo,
					   bool is_static)
			: base (klass.File, pinfo, index, index, is_static)
		{
			Klass = klass;
			PropertyInfo = pinfo;
			type = File.MonoLanguage.LookupMonoType (pinfo.PropertyType);

			if (PropertyInfo.GetMethod != null) {
				Cecil.IMethodDefinition getter = PropertyInfo.GetMethod;
				int pos = C.MonoDebuggerSupport.GetMethodIndex (getter);
				GetterType = new MonoFunctionType (File, Klass, getter, pos - 1);
			}

			if (PropertyInfo.SetMethod != null) {
				Cecil.IMethodDefinition setter = PropertyInfo.SetMethod;
				int pos = C.MonoDebuggerSupport.GetMethodIndex (setter);
				SetterType = new MonoFunctionType (File, Klass, setter, pos - 1);
			}
		}

		public override MonoType Type {
			get { return type; }
		}

		public bool CanRead {
			get { return PropertyInfo.GetMethod != null; }
		}

		ITargetFunctionType ITargetPropertyInfo.Getter {
			get {
				if (PropertyInfo.GetMethod == null)
					throw new InvalidOperationException ();

				return GetterType;
			}
		}

		public bool CanWrite {
			get { return PropertyInfo.SetMethod != null; }
		}

		ITargetFunctionType ITargetPropertyInfo.Setter {
			get {
				if (PropertyInfo.SetMethod == null)
					throw new InvalidOperationException ();

				return SetterType;
			}
		}

		internal ITargetObject Get (TargetLocation location)
		{
			if (PropertyInfo.GetMethod == null)
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
			if (PropertyInfo.SetMethod == null)
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
