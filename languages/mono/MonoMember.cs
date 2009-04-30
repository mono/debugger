using System;
using System.Text;
using System.Diagnostics;
using System.Collections;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	[Serializable]
	internal class MonoFieldInfo : TargetFieldInfo
	{
		[NonSerialized]
		public readonly Cecil.FieldDefinition FieldInfo;
		DebuggerBrowsableState? browsable_state = null;
		DebuggerDisplayAttribute debugger_display;
		bool is_compiler_generated;

		public MonoFieldInfo (IMonoStructType type, TargetType field_type, int pos,
				      Cecil.FieldDefinition finfo)
			: base (field_type, finfo.Name, pos, finfo.IsStatic,
				GetAccessibility (finfo), pos, 0, finfo.HasConstant)
		{
			FieldInfo = finfo;

			DebuggerTypeProxyAttribute type_proxy;
			MonoSymbolFile.CheckCustomAttributes (finfo,
							      out browsable_state,
							      out debugger_display,
							      out type_proxy,
							      out is_compiler_generated);
		}

		public override DebuggerBrowsableState? DebuggerBrowsableState {
			get { return browsable_state; }
		}

		public override DebuggerDisplayAttribute DebuggerDisplayAttribute {
			get { return debugger_display; }
		}

		internal static TargetMemberAccessibility GetAccessibility (Cecil.FieldDefinition field)
		{
			switch (field.Attributes & Cecil.FieldAttributes.FieldAccessMask) {
			case Cecil.FieldAttributes.Public:
				return TargetMemberAccessibility.Public;
			case Cecil.FieldAttributes.Family:
			case Cecil.FieldAttributes.FamANDAssem:
				return TargetMemberAccessibility.Protected;
			case Cecil.FieldAttributes.Assembly:
			case Cecil.FieldAttributes.FamORAssem:
				return TargetMemberAccessibility.Internal;
			default:
				return TargetMemberAccessibility.Private;
			}
		}

		public override bool IsCompilerGenerated {
			get { return is_compiler_generated; }
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

		public MonoEnumInfo (MonoEnumType type, TargetType field_type, int index, int pos,
				     Cecil.FieldDefinition finfo)
			: base (field_type, finfo.Name, index, finfo.IsStatic,
				MonoFieldInfo.GetAccessibility (finfo),
				pos, 0, finfo.HasConstant)
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

		private MonoMethodInfo (IMonoStructType klass, int index, Cecil.MethodDefinition minfo,
					MonoFunctionType type)
			: base (type, MonoFunctionType.GetMethodName (minfo), index,
				minfo.IsStatic, GetAccessibility (minfo), type.FullName)
		{
			FunctionType = type;
		}

		internal static MonoMethodInfo Create (IMonoStructType klass, int index,
						       Cecil.MethodDefinition minfo)
		{
			MonoFunctionType type = klass.LookupFunction (minfo);
			return new MonoMethodInfo (klass, index, minfo, type);
		}

		internal static TargetMemberAccessibility GetAccessibility (Cecil.MethodDefinition method)
		{
			switch (method.Attributes & Cecil.MethodAttributes.MemberAccessMask) {
			case Cecil.MethodAttributes.Public:
				return TargetMemberAccessibility.Public;
			case Cecil.MethodAttributes.Family:
			case Cecil.MethodAttributes.FamANDAssem:
				return TargetMemberAccessibility.Protected;
			case Cecil.MethodAttributes.Assem:
			case Cecil.MethodAttributes.FamORAssem:
				return TargetMemberAccessibility.Internal;
			default:
				return TargetMemberAccessibility.Private;
			}
		}
	}

	[Serializable]
	internal class MonoEventInfo : TargetEventInfo
	{
		public readonly MonoClassType Klass;
		public readonly MonoFunctionType AddType, RemoveType, RaiseType;

		private MonoEventInfo (MonoClassType klass, int index, Cecil.EventDefinition einfo,
				       TargetType type, bool is_static,
				       TargetMemberAccessibility accessibility, MonoFunctionType add,
				       MonoFunctionType remove, MonoFunctionType raise)
			: base (type, einfo.Name, index, is_static, accessibility, add, remove, raise)
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

			TargetMemberAccessibility accessibility = TargetMemberAccessibility.Private;
			if (einfo.AddMethod != null) {
				add = klass.LookupFunction (einfo.AddMethod);
				is_static = einfo.AddMethod.IsStatic;
				accessibility = MonoMethodInfo.GetAccessibility (einfo.AddMethod);
			} else
				add = null;

			if (einfo.RemoveMethod != null) {
				remove = klass.LookupFunction (einfo.RemoveMethod);
				is_static = einfo.RemoveMethod.IsStatic;
				accessibility = MonoMethodInfo.GetAccessibility (einfo.RemoveMethod);
			} else
				remove = null;

			if (einfo.InvokeMethod != null) {
				raise = klass.LookupFunction (einfo.InvokeMethod);
				is_static = einfo.InvokeMethod.IsStatic;
				accessibility = MonoMethodInfo.GetAccessibility (einfo.InvokeMethod);
			} else
				raise = null;

			return new MonoEventInfo (
				klass, index, einfo, type, is_static, accessibility, add, remove, raise);
		}
	}

	[Serializable]
	internal class MonoPropertyInfo : TargetPropertyInfo
	{
		public readonly IMonoStructType Klass;
		public readonly MonoFunctionType GetterType, SetterType;
		DebuggerBrowsableState? browsable_state = null;
		DebuggerDisplayAttribute debugger_display;

		private MonoPropertyInfo (TargetType type, IMonoStructType klass, int index,
					  bool is_static, Cecil.PropertyDefinition pinfo,
					  TargetMemberAccessibility accessibility,
					  MonoFunctionType getter, MonoFunctionType setter)
			: base (type, pinfo.Name, index, is_static, accessibility, getter, setter)
		{
			this.Klass = klass;
			this.GetterType = getter;
			this.SetterType = setter;

			bool is_compiler_generated;
			DebuggerTypeProxyAttribute type_proxy;
			MonoSymbolFile.CheckCustomAttributes (pinfo,
							      out browsable_state,
							      out debugger_display,
							      out type_proxy,
							      out is_compiler_generated);
		}

		public override DebuggerBrowsableState? DebuggerBrowsableState {
			get { return browsable_state; }
		}

		public override DebuggerDisplayAttribute DebuggerDisplayAttribute {
			get { return debugger_display; }
		}

		internal static MonoPropertyInfo Create (IMonoStructType klass, int index,
							 Cecil.PropertyDefinition pinfo)
		{
			TargetType type = klass.File.MonoLanguage.LookupMonoType (pinfo.PropertyType);

			bool is_static = false;
			MonoFunctionType getter, setter;
			TargetMemberAccessibility accessibility = TargetMemberAccessibility.Private;
			if (pinfo.SetMethod != null) {
				setter = klass.LookupFunction (pinfo.SetMethod);
				is_static = pinfo.SetMethod.IsStatic;
				accessibility = MonoMethodInfo.GetAccessibility (pinfo.SetMethod);
			} else
				setter = null;

			if (pinfo.GetMethod != null) {
				getter = klass.LookupFunction (pinfo.GetMethod);
				is_static = pinfo.GetMethod.IsStatic;
				accessibility = MonoMethodInfo.GetAccessibility (pinfo.GetMethod);
			} else
				getter = null;

			return new MonoPropertyInfo (
				type, klass, index, is_static, pinfo, accessibility, getter, setter);
		}
	}
}
