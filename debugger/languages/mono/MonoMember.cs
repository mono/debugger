using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	[Serializable]
	internal class MonoFieldInfo : TargetFieldInfo
	{
		[NonSerialized]
		public readonly Cecil.FieldDefinition FieldInfo;
		public readonly MonoClassType DeclaringType;

		public MonoFieldInfo (MonoClassType type, TargetType field_type, int pos,
				      Cecil.FieldDefinition finfo)
			: base (field_type, finfo.Name, pos, finfo.IsStatic, pos, 0, finfo.HasConstant)
		{
			FieldInfo = finfo;
			DeclaringType = type;
		}

		public override object ConstValue {
			get {
				if (FieldInfo.HasConstant)
					return FieldInfo.Constant;
				else
					throw new InvalidOperationException ();
			}
		}
	}

	[Serializable]
	internal class MonoEnumInfo : TargetEnumInfo
	{
		[NonSerialized]
		public readonly Cecil.FieldDefinition FieldInfo;
		MonoEnumType type;

		public MonoEnumInfo (MonoEnumType type, TargetType field_type, int index, int pos,
				     Cecil.FieldDefinition finfo)
			: base (field_type, finfo.Name, index, finfo.IsStatic, pos, 0, finfo.HasConstant)
		{
			FieldInfo = finfo;
			this.type = type;
		}

		public override object ConstValue {
			get {
				if (FieldInfo.HasConstant)
					return FieldInfo.Constant;
				else
					throw new InvalidOperationException ();
			}
		}
	}

	[Serializable]
	internal class MonoMethodInfo : TargetMethodInfo
	{
		public readonly MonoFunctionType FunctionType;
		public readonly MonoClassType Klass;

		private MonoMethodInfo (MonoClassType klass, int index, Cecil.MethodDefinition minfo,
					MonoFunctionType type)
			: base (type, MonoFunctionType.GetMethodName (minfo), index,
				minfo.IsStatic, type.FullName)
		{
			Klass = klass;
			FunctionType = type;
		}

		internal static MonoMethodInfo Create (MonoClassType klass, int index,
						       Cecil.MethodDefinition minfo)
		{
			MonoFunctionType type = klass.File.LookupFunction (klass, minfo);
			return new MonoMethodInfo (klass, index, minfo, type);
		}
	}

	[Serializable]
	internal class MonoEventInfo : TargetEventInfo
	{
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType AddType, RemoveType, RaiseType;

		private MonoEventInfo (MonoClassType klass, int index, Cecil.EventDefinition einfo,
				       TargetType type, bool is_static,  MonoFunctionType add,
				       MonoFunctionType remove, MonoFunctionType raise)
			: base (type, einfo.Name, index, is_static, add, remove, raise)
		{
			this.Klass = klass;
			this.AddType = add;
			this.RemoveType = remove;
			this.RaiseType = raise;
		}

		internal static MonoEventInfo Create (MonoClassType klass, int index,
						      Cecil.EventDefinition einfo)
		{
			TargetType type = klass.File.MonoLanguage.LookupMonoType (einfo.EventType);

			bool is_static = false;
			MonoFunctionType add, remove, raise;
			if (einfo.AddMethod != null) {
				add = klass.File.LookupFunction (klass, einfo.AddMethod);
				is_static = einfo.AddMethod.IsStatic;
			} else
				add = null;

			if (einfo.RemoveMethod != null) {
				remove = klass.File.LookupFunction (klass, einfo.RemoveMethod);
				is_static = einfo.RemoveMethod.IsStatic;
			} else
				remove = null;

			if (einfo.InvokeMethod != null) {
				raise = klass.File.LookupFunction (klass, einfo.InvokeMethod);
				is_static = einfo.InvokeMethod.IsStatic;
			} else
				raise = null;

			return new MonoEventInfo (
				klass, index, einfo, type, is_static, add, remove, raise);
		}
	}

	[Serializable]
	internal class MonoPropertyInfo : TargetPropertyInfo
	{
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType GetterType, SetterType;

		private MonoPropertyInfo (TargetType type, MonoClassType klass, int index,
					  bool is_static, Cecil.PropertyDefinition pinfo,
					  MonoFunctionType getter, MonoFunctionType setter)
			: base (type, pinfo.Name, index, is_static, getter, setter)
		{
			this.Klass = klass;
			this.GetterType = getter;
			this.SetterType = setter;
		}

		internal static MonoPropertyInfo Create (MonoClassType klass, int index,
							 Cecil.PropertyDefinition pinfo)
		{
			TargetType type = klass.File.MonoLanguage.LookupMonoType (pinfo.PropertyType);

			bool is_static = false;
			MonoFunctionType getter, setter;
			if (pinfo.GetMethod != null) {
				getter = klass.File.LookupFunction (klass, pinfo.GetMethod);
				is_static = pinfo.GetMethod.IsStatic;
			} else
				getter = null;

			if (pinfo.SetMethod != null) {
				setter = klass.File.LookupFunction (klass, pinfo.SetMethod);
				is_static = pinfo.SetMethod.IsStatic;
			} else
				setter = null;

			return new MonoPropertyInfo (
				type, klass, index, is_static, pinfo, getter, setter);
		}
	}
}
