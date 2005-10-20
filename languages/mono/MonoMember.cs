using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	[Serializable]
	internal class MonoFieldInfo : TargetFieldInfo
	{
		[NonSerialized]
		public readonly Cecil.IFieldDefinition FieldInfo;

		public MonoFieldInfo (TargetType type, int index, int pos,
				      Cecil.IFieldDefinition finfo)
			: base (type, finfo.Name, index, finfo.IsStatic, pos, 0, finfo.HasConstant)
		{
			FieldInfo = finfo;
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

		private MonoMethodInfo (MonoClassType klass, int index, Cecil.IMethodDefinition minfo,
					string full_name, MonoFunctionType type)
			: base (type, minfo.Name, index, minfo.IsStatic, full_name)
		{
			Klass = klass;
			FunctionType = type;
		}

		internal static MonoMethodInfo Create (MonoClassType klass, int index,
						       Cecil.IMethodDefinition minfo)
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

			string fname = String.Format (
				"{0}.{1}({2})", klass.Name, minfo.Name, sb.ToString ());

			MonoFunctionType type = new MonoFunctionType (klass, minfo, fname);
			return new MonoMethodInfo (klass, index, minfo, fname, type);
		}
	}

	[Serializable]
	internal class MonoEventInfo : TargetEventInfo
	{
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType AddType, RemoveType, RaiseType;

		private MonoEventInfo (MonoClassType klass, int index, Cecil.IEventDefinition einfo,
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
						      Cecil.IEventDefinition einfo, bool is_static)
		{
			TargetType type = klass.File.MonoLanguage.LookupMonoType (einfo.EventType);

			MonoFunctionType add, remove, raise;
			if (einfo.AddMethod != null)
				add = new MonoFunctionType (klass, einfo.AddMethod, einfo.Name);
			else
				add = null;

			if (einfo.RemoveMethod != null)
				remove = new MonoFunctionType (klass, einfo.RemoveMethod, einfo.Name);
			else
				remove = null;

			if (einfo.InvokeMethod != null)
				raise = new MonoFunctionType (klass, einfo.InvokeMethod, einfo.Name);
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
					  Cecil.IPropertyDefinition pinfo, bool is_static,
					  MonoFunctionType getter, MonoFunctionType setter)
			: base (type, pinfo.Name, index, is_static, getter, setter)
		{
			this.Klass = klass;
			this.GetterType = getter;
			this.SetterType = setter;
		}

		internal static MonoPropertyInfo Create (MonoClassType klass, int index,
							 Cecil.IPropertyDefinition pinfo,
							 bool is_static)
		{
			TargetType type = klass.File.MonoLanguage.LookupMonoType (pinfo.PropertyType);

			MonoFunctionType getter, setter;
			if (pinfo.GetMethod != null)
				getter = new MonoFunctionType (klass, pinfo.GetMethod, pinfo.Name);
			else
				getter = null;

			if (pinfo.SetMethod != null)
				setter = new MonoFunctionType (klass, pinfo.SetMethod, pinfo.Name);
			else
				setter = null;

			return new MonoPropertyInfo (
				type, klass, index, pinfo, is_static, getter, setter);
		}
	}
}
