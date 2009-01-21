using System;
using System.Collections;
using System.Collections.Generic;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : TargetClass
	{
		public readonly MonoSymbolFile SymbolFile;
		public readonly TargetAddress KlassAddress;
		public readonly TargetAddress GenericContainer;
		public readonly TargetAddress GenericClass;

		public readonly Cecil.TypeDefinition CecilType;

		TargetType type;
		IMonoStructType struct_type;

		MonoClassInfo parent_info;
		TargetAddress parent_klass = TargetAddress.Null;
		TargetType[] field_types;
		MonoFieldInfo[] fields;
		int[] field_offsets;
		MonoMethodInfo[] methods;
		MonoPropertyInfo[] properties;
		Dictionary<int,TargetAddress> methods_by_token;

		public static MonoClassInfo ReadClassInfo (MonoLanguageBackend mono,
							   TargetMemoryAccess target,
							   TargetAddress klass)
		{
			TargetAddress image = mono.MonoRuntime.MonoClassGetMonoImage (target, klass);
			MonoSymbolFile file = mono.GetImage (image);
			if (file == null)
				throw new InternalError ();

			int token = mono.MonoRuntime.MonoClassGetToken (target, klass);
			if ((token & 0xff000000) != 0x02000000)
				throw new InternalError ();

			Cecil.TypeDefinition typedef;
			typedef = (Cecil.TypeDefinition) file.ModuleDefinition.LookupByToken (
				Cecil.Metadata.TokenType.TypeDef, token & 0x00ffffff);
			if (typedef == null)
				throw new InternalError ();

			MonoClassInfo info = new MonoClassInfo (file, typedef, target, klass);
			if (info.IsGenericClass) {
				info.struct_type = file.MonoLanguage.ReadGenericClass (
					target, info.GenericClass);
				info.type = info.struct_type.Type;
			} else if ((file == mono.BuiltinTypes.Corlib) &&
				   (typedef.FullName == "System.Decimal")) {
				MonoFundamentalType ftype = mono.BuiltinTypes.DecimalType;

				if (ftype.ClassType == null) {
					MonoClassType ctype = new MonoClassType (file, typedef, info);
					((IMonoStructType) ctype).ClassInfo = info;
					ftype.SetClass (ctype);
				}

				info.struct_type = (IMonoStructType) ftype.ClassType;
				info.type = ftype;
			} else {
				info.type = file.LookupMonoType (typedef);
				if (info.type is TargetStructType)
					info.struct_type = (IMonoStructType) info.type;
				else
					info.struct_type = (IMonoStructType) info.type.ClassType;
			}
			info.struct_type.ClassInfo = info;
			return info;
		}

		public static MonoClassInfo ReadCoreType (MonoSymbolFile file,
							  Cecil.TypeDefinition typedef,
							  TargetMemoryAccess target,
							  TargetAddress klass,
							  out MonoClassType type)
		{
			MonoClassInfo info = new MonoClassInfo (file, typedef, target, klass);
			type = new MonoClassType (file, typedef, info);
			((IMonoStructType) type).ClassInfo = info;
			info.struct_type = type;
			info.type = type;
			return info;
		}

		protected MonoClassInfo (MonoSymbolFile file, Cecil.TypeDefinition typedef,
					 TargetMemoryAccess target, TargetAddress klass)
		{
			this.SymbolFile = file;
			this.KlassAddress = klass;
			this.CecilType = typedef;

			parent_klass = MonoRuntime.MonoClassGetParent (target, klass);
			GenericClass = MonoRuntime.MonoClassGetGenericClass (target, klass);
			GenericContainer = MonoRuntime.MonoClassGetGenericContainer (target, klass);
		}

		protected MonoRuntime MonoRuntime {
			get { return SymbolFile.MonoLanguage.MonoRuntime; }
		}

		public override TargetType RealType {
			get { return type; }
		}

		public override TargetStructType Type {
			get { return struct_type.Type; }
		}

		public bool IsGenericClass {
			get { return !GenericClass.IsNull; }
		}

		internal MonoFieldInfo[] GetFields (TargetMemoryAccess target)
		{
			if (fields != null)
				return fields;

			int field_count = MonoRuntime.MonoClassGetFieldCount (target, KlassAddress);

			fields = new MonoFieldInfo [field_count];
			field_offsets = new int [field_count];
			field_types = new TargetType [field_count];

			for (int i = 0; i < field_count; i++) {
				Cecil.FieldDefinition field = CecilType.Fields [i];

				TargetAddress type_addr = MonoRuntime.MonoClassGetFieldType (
					target, KlassAddress, i);

				field_types [i] = SymbolFile.MonoLanguage.ReadType (target, type_addr);
				field_offsets [i] = MonoRuntime.MonoClassGetFieldOffset (
					target, KlassAddress, i);

				fields [i] = new MonoFieldInfo (struct_type, field_types [i], i, field);
			}

			return fields;
		}

		public override TargetFieldInfo[] GetFields (Thread thread)
		{
			if (fields != null)
				return fields;

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					GetFields (target);
					return null;
			});

			return fields;
		}

		public override TargetObject GetField (Thread thread,
						       TargetStructObject instance,
						       TargetFieldInfo field)
		{
			if (field.HasConstValue)
				return SymbolFile.MonoLanguage.CreateInstance (thread, field.ConstValue);

			if (field.IsStatic) {
				return GetStaticField (thread, field);
			} else {
				if (instance == null)
					throw new InvalidOperationException ();

				return (TargetObject) thread.ThreadServant.DoTargetAccess (
					delegate (TargetMemoryAccess target)  {
						return GetInstanceField (target, instance, field);
				});
			}
		}

		internal TargetObject GetInstanceField (TargetMemoryAccess target,
							TargetStructObject instance,
							TargetFieldInfo field)
		{
			GetFields (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!Type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (offset);

			TargetAddress orig_addr = field_loc.GetAddress (target);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			TargetAddress addr = field_loc.GetAddress (target);

			if (field_loc.HasAddress && field_loc.GetAddress (target).IsNull)
				return new MonoNullObject (type, field_loc);

			return type.GetObject (target, field_loc);
		}

		internal TargetObject GetStaticField (Thread thread, TargetFieldInfo field)
		{
			TargetAddress data_address = thread.CallMethod (
				SymbolFile.MonoLanguage.MonoDebuggerInfo.ClassGetStaticFieldData,
				KlassAddress, 0);

			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetStaticField (target, field, data_address);
			});
		}

		internal TargetObject GetStaticField (TargetMemoryAccess target, TargetFieldInfo field,
						      TargetAddress data_address)
		{
			GetFields (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			return type.GetObject (target, field_loc);
		}

		public override void SetField (Thread thread, TargetStructObject instance,
					       TargetFieldInfo field, TargetObject value)
		{
			if (field.IsStatic) {
				if (instance != null)
					throw new InvalidOperationException ();

				SetStaticField (thread, field, value);
			} else {
				if (instance == null)
					throw new InvalidOperationException ();

				thread.ThreadServant.DoTargetAccess (
					delegate (TargetMemoryAccess target)  {
						SetInstanceField (target, instance, field, value);
						return null;
				});
			}
		}

		internal void SetInstanceField (TargetMemoryAccess target,
						TargetStructObject instance,
						TargetFieldInfo field, TargetObject obj)
		{
			GetFields (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!Type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			type.SetObject (target, field_loc, obj);
		}

		internal void SetStaticField (Thread thread, TargetFieldInfo field,
					      TargetObject obj)
		{
			TargetAddress data_address = thread.CallMethod (
				SymbolFile.MonoLanguage.MonoDebuggerInfo.ClassGetStaticFieldData,
				KlassAddress, 0);

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					SetStaticField (target, field, data_address, obj);
					return null;
			});
		}

		internal void SetStaticField (TargetMemoryAccess target, TargetFieldInfo field,
					      TargetAddress data_address, TargetObject obj)
		{
			GetFields (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			type.SetObject (target, field_loc, obj);
		}

		internal MonoPropertyInfo[] GetProperties (TargetMemoryAccess target)
		{
			if (properties != null)
				return properties;

			properties = new MonoPropertyInfo [CecilType.Properties.Count];

			for (int i = 0; i < CecilType.Properties.Count; i++) {
				Cecil.PropertyDefinition prop = CecilType.Properties [i];
				properties [i] = MonoPropertyInfo.Create (struct_type, i, prop);
			}

			return properties;
		}

		public override TargetPropertyInfo[] GetProperties (Thread thread)
		{
			return (MonoPropertyInfo []) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					return GetProperties (target);
			});
		}

		void get_methods (TargetMemoryAccess target)
		{
			if (methods_by_token != null)
				return;

			try {
				if (!MonoRuntime.MonoClassHasMethods (target, KlassAddress))
					return;

				int count = MonoRuntime.MonoClassGetMethodCount (target, KlassAddress);

				methods_by_token = new Dictionary<int,TargetAddress> ();

				for (int i = 0; i < count; i++) {
					TargetAddress address = MonoRuntime.MonoClassGetMethod (
						target, KlassAddress, i);
					int mtoken = MonoRuntime.MonoMethodGetToken (target, address);
					if (mtoken != 0)
						methods_by_token.Add (mtoken, address);
				}

				methods = new MonoMethodInfo [CecilType.Methods.Count];
				for (int i = 0; i < methods.Length; i ++) {
					Cecil.MethodDefinition m = CecilType.Methods [i];
					methods [i] = MonoMethodInfo.Create (struct_type, i, m);
				}
			} catch {
				methods_by_token = null;
				methods = null;
				throw;
			}
		}

		public TargetAddress GetMethodAddress (TargetMemoryAccess target, int token)
		{
			get_methods (target);
			if ((methods_by_token == null) || !methods_by_token.ContainsKey (token))
				return TargetAddress.Null;
			return methods_by_token [token];
		}

		public override TargetMethodInfo[] GetMethods (Thread thread)
		{
			if (methods != null)
				return methods;

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					get_methods (target);
					return null;
			});

			return methods;
		}

		void get_parent (TargetMemoryAccess target)
		{
			parent_klass = MonoRuntime.MonoClassGetParent (target, KlassAddress);
			if (parent_klass.IsNull)
				return;

			parent_info = ReadClassInfo (SymbolFile.MonoLanguage, target, parent_klass);
		}

		public override bool HasParent {
			get { return !parent_klass.IsNull; }
		}

		internal MonoClassInfo GetParent (TargetMemoryAccess target)
		{
			if (parent_info != null)
				return parent_info;

			get_parent (target);
			return parent_info;
		}

		public override TargetClass GetParent (Thread thread)
		{
			if (parent_info != null)
				return parent_info;

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					get_parent (target);
					return null;
			});
			return parent_info;
		}

		public override string ToString ()
		{
			return String.Format ("ClassInfo ({0}:{1}:{2}:{3})",
					      SymbolFile.Assembly.Name.Name, KlassAddress,
					      GenericContainer, GenericClass);
		}
	}
}
