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

		public MonoFieldInfo (TargetType type, int index, int pos,
				      Cecil.FieldDefinition finfo)
			: base (type, finfo.Name, index, finfo.IsStatic, pos, 0, finfo.HasConstant)
		{
			FieldInfo = finfo;
		}

		public MonoFieldInfo InflateField (MonoGenericContext context)
		{
			if (!Type.ContainsGenericParameters)
				return this;

			try {
			TargetType inflated_type = Type.InflateType (context);
			return new MonoFieldInfo (inflated_type, Index, Position, FieldInfo);
			} catch (Exception ex) {
				Console.WriteLine ("FUCK: {0}", ex);
				throw;
			}
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
				       bool is_static, TargetType type, MonoFunctionType add,
				       MonoFunctionType remove, MonoFunctionType raise)
			: base (type, einfo.Name, index, is_static, add, remove, raise)
		{
			this.Klass = klass;
			this.AddType = add;
			this.RemoveType = remove;
			this.RaiseType = raise;
		}

		internal static MonoEventInfo Create (MonoClassType klass, int index,
						      Cecil.EventDefinition einfo, bool is_static)
		{
			TargetType type = klass.File.MonoLanguage.LookupMonoType (einfo.EventType);

			MonoFunctionType add, remove, raise;
			if (einfo.AddMethod != null)
				add = klass.File.LookupFunction (klass, einfo.AddMethod);
			else
				add = null;

			if (einfo.RemoveMethod != null)
				remove = klass.File.LookupFunction (klass, einfo.RemoveMethod);
			else
				remove = null;

			if (einfo.InvokeMethod != null)
				raise = klass.File.LookupFunction (klass, einfo.InvokeMethod);
			else
				raise = null;

			return new MonoEventInfo (klass, index, einfo, is_static,
						  type, add, remove, raise);
		}
	}

	[Serializable]
	internal class MonoPropertyInfo : TargetPropertyInfo
	{
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType GetterType, SetterType;

		private MonoPropertyInfo (TargetType type, MonoClassType klass, int index,
					  Cecil.PropertyDefinition pinfo, bool is_static,
					  MonoFunctionType getter, MonoFunctionType setter)
			: base (type, pinfo.Name, index, is_static, getter, setter)
		{
			this.Klass = klass;
			this.GetterType = getter;
			this.SetterType = setter;
		}

		internal static MonoPropertyInfo Create (MonoClassType klass, int index,
							 Cecil.PropertyDefinition pinfo,
							 bool is_static)
		{
			TargetType type = klass.File.MonoLanguage.LookupMonoType (pinfo.PropertyType);

			MonoFunctionType getter, setter;
			if (pinfo.GetMethod != null)
				getter = klass.File.LookupFunction (klass, pinfo.GetMethod);
			else
				getter = null;

			if (pinfo.SetMethod != null)
				setter = klass.File.LookupFunction (klass, pinfo.SetMethod);
			else
				setter = null;

			return new MonoPropertyInfo (
				type, klass, index, pinfo, is_static, getter, setter);
		}
	}
}
