using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : DebuggerMarshalByRefObject
	{
		public readonly MonoSymbolFile SymbolFile;
		public readonly TargetAddress KlassAddress;
		public readonly TargetAddress GenericContainer;
		public readonly TargetAddress GenericClass;

		public readonly Cecil.TypeDefinition CecilType;

		MonoClassType type;

		MonoClassInfo parent_info;
		TargetAddress parent_klass = TargetAddress.Null;
		TargetType[] field_types;
		int[] field_offsets;
		Hashtable methods;

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
			info.type = file.LookupMonoClass (typedef);
			return info;
		}

		public static MonoClassInfo ReadClassInfo (MonoSymbolFile file,
							   Cecil.TypeDefinition typedef,
							   TargetMemoryAccess target,
							   TargetAddress klass,
							   out MonoClassType type)
		{
			MonoClassInfo info = new MonoClassInfo (file, typedef, target, klass);
			type = new MonoClassType (file, typedef, info);
			info.type = type;
			return info;
		}

		protected MonoClassInfo (MonoSymbolFile file, Cecil.TypeDefinition typedef,
					 TargetMemoryAccess target, TargetAddress klass)
		{
			this.SymbolFile = file;
			this.KlassAddress = klass;
			this.CecilType = typedef;

			GenericClass = MonoRuntime.MonoClassGetGenericClass (target, klass);
			GenericContainer = MonoRuntime.MonoClassGetGenericContainer (target, klass);
		}

		protected MonoRuntime MonoRuntime {
			get { return SymbolFile.MonoLanguage.MonoRuntime; }
		}

		public MonoClassType ClassType {
			get { return type; }
		}

		public bool IsGenericClass {
			get { return !GenericClass.IsNull; }
		}

		void get_field_offsets (TargetMemoryAccess target)
		{
			if (field_offsets != null)
				return;

			int field_count = MonoRuntime.MonoClassGetFieldCount (target, KlassAddress);

			field_offsets = new int [field_count];
			field_types = new TargetType [field_count];

			for (int i = 0; i < field_count; i++) {
				TargetAddress type_addr = MonoRuntime.MonoClassGetFieldType (
					target, KlassAddress, i);

				field_types [i] = OldMonoRuntime.ReadType (
					SymbolFile.MonoLanguage, target, type_addr);
				field_offsets [i] = MonoRuntime.MonoClassGetFieldOffset (
					target, KlassAddress, i);
			}
		}

		void get_field_offsets (Thread thread)
		{
			if (field_offsets != null)
				return;

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					get_field_offsets (target);
					return null;
			});
		}

		internal TargetObject GetField (TargetMemoryAccess target, TargetLocation location,
						TargetFieldInfo field)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!ClassType.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			if (field_loc.HasAddress && field_loc.GetAddress (target).IsNull)
				return null;

			return type.GetObject (target, field_loc);
		}

		internal void SetField (TargetMemoryAccess target, TargetLocation location,
					TargetFieldInfo field, TargetObject obj)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!ClassType.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			type.SetObject (target, field_loc, obj);
		}

		public TargetObject GetStaticField (Thread thread, TargetFieldInfo field)
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
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			return type.GetObject (target, field_loc);
		}

		public void SetStaticField (Thread thread, TargetFieldInfo field,
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
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			type.SetObject (target, field_loc, obj);
		}

		void get_methods (TargetMemoryAccess target)
		{
			if (methods != null)
				return;

			methods = new Hashtable ();
			int method_count = MonoRuntime.MonoClassGetMethodCount (target, KlassAddress);

			for (int i = 0; i < method_count; i++) {
				TargetAddress address = MonoRuntime.MonoClassGetMethod (
					target, KlassAddress, i);

				int mtoken = MonoRuntime.MonoMethodGetToken (target, address);
				if (mtoken == 0)
					continue;

				methods.Add (mtoken, address);
			}
		}

		public TargetAddress GetMethodAddress (TargetMemoryAccess target, int token)
		{
			get_methods (target);
			if (!methods.Contains (token))
				throw new InternalError ();
			return (TargetAddress) methods [token];
		}

		void get_parent (TargetMemoryAccess target)
		{
			parent_klass = MonoRuntime.MonoClassGetParent (target, KlassAddress);

			parent_info = ReadClassInfo (SymbolFile.MonoLanguage, target, parent_klass);
		}

		public MonoClassInfo GetParent (Thread thread)
		{
			if (!parent_klass.IsNull)
				return parent_info;

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					get_parent (target);
					return null;
			});
			return parent_info;
		}
	}
}
