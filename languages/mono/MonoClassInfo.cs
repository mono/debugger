using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : TargetClass
	{
		public readonly MonoSymbolFile SymbolFile;
		public readonly TargetAddress KlassAddress;
		public readonly TargetAddress GenericContainer;
		public readonly TargetAddress GenericClass;

		public readonly Cecil.TypeDefinition CecilType;

		MonoClassType type;

		MonoClassInfo parent_info;
		TargetAddress parent_klass = TargetAddress.Null;

		MonoFieldInfo[] fields;
		MonoFieldInfo[] static_fields;
		int num_fields = 0, num_sfields = 0;

		TargetType[] field_types;
		int[] field_offsets;

		Hashtable methods;

		public static MonoClassInfo ReadClassInfo (MonoLanguageBackend mono,
							   TargetMemoryAccess target,
							   TargetAddress klass_address)
		{
			TargetReader reader = new TargetReader (
				target.ReadMemory (klass_address, mono.MonoMetadataInfo.KlassSize));

			TargetAddress image = reader.PeekAddress (mono.MonoMetadataInfo.KlassImageOffset);
			MonoSymbolFile file = mono.GetImage (image);
			if (file == null)
				throw new InternalError ();

			int token = reader.PeekInteger (mono.MonoMetadataInfo.KlassTokenOffset);
			if ((token & 0xff000000) != 0x02000000)
				throw new InternalError ();

			Cecil.TypeDefinition typedef;
			typedef = (Cecil.TypeDefinition) file.ModuleDefinition.LookupByToken (
				Cecil.Metadata.TokenType.TypeDef, token & 0x00ffffff);
			if (typedef == null)
				throw new InternalError ();

			MonoClassInfo info = new MonoClassInfo (
				file, typedef, target, reader, klass_address);
			Console.WriteLine ("READ CLASS INFO: {0} {1} {2} {3}",
					   klass_address, typedef, info.GenericContainer,
					   info.GenericClass);
			info.type = file.LookupMonoClass (typedef);
			return info;
		}

		public static MonoClassInfo ReadClassInfo (MonoSymbolFile file,
							   Cecil.TypeDefinition typedef,
							   TargetMemoryAccess target,
							   TargetAddress klass_address,
							   out MonoClassType type)
		{
			MonoMetadataInfo metadata = file.MonoLanguage.MonoMetadataInfo;
			TargetReader reader = new TargetReader (
				target.ReadMemory (klass_address, metadata.KlassSize));

			MonoClassInfo info = new MonoClassInfo (
				file, typedef, target, reader, klass_address);

			type = new MonoClassType (file, typedef, info);
			info.type = type;
			return info;
		}

		protected MonoClassInfo (MonoSymbolFile file, Cecil.TypeDefinition typedef,
					 TargetMemoryAccess target, TargetReader reader,
					 TargetAddress klass)
		{
			this.SymbolFile = file;
			this.KlassAddress = klass;
			this.CecilType = typedef;

			MonoMetadataInfo info = file.MonoLanguage.MonoMetadataInfo;

			GenericClass = reader.PeekAddress (info.KlassGenericClassOffset);
			GenericContainer = reader.PeekAddress (info.KlassGenericContainerOffset);
		}

		public override TargetClassType Type {
			get { return type; }
		}

		internal MonoClassType MonoClassType {
			get { return type; }
		}

		public bool IsGenericClass {
			get { return !GenericClass.IsNull; }
		}

		void get_fields ()
		{
			if (fields != null)
				return;

			foreach (Cecil.FieldDefinition field in CecilType.Fields) {
				if (field.IsStatic)
					num_sfields++;
				else
					num_fields++;
			}

			fields = new MonoFieldInfo [num_fields];
			static_fields = new MonoFieldInfo [num_sfields];

			int pos = 0, spos = 0, i = 0;
			foreach (Cecil.FieldDefinition field in CecilType.Fields) {
				TargetType ftype = SymbolFile.MonoLanguage.LookupMonoType (field.FieldType);

				if (ftype == null)
					ftype = SymbolFile.MonoLanguage.VoidType;

				if (field.IsStatic) {
					static_fields [spos] = new MonoFieldInfo (
						type, ftype, spos, i, field);
					spos++;
				} else {
					fields [pos] = new MonoFieldInfo (
						type, ftype, pos, i, field);
					pos++;
				}

				i++;
			}
		}

		public override TargetFieldInfo[] Fields {
			get {
				get_fields ();
				return fields;
			}
		}

		public override TargetFieldInfo[] StaticFields {
			get {
				get_fields ();
				return static_fields;
			}
		}

		void get_field_offsets (TargetMemoryAccess target)
		{
			if (field_offsets != null)
				return;

			MonoMetadataInfo metadata = SymbolFile.MonoLanguage.MonoMetadataInfo;

			TargetAddress field_info = target.ReadAddress (
				KlassAddress + metadata.KlassFieldOffset);
			int field_count = target.ReadInteger (
				KlassAddress + metadata.KlassFieldCountOffset);

			TargetReader field_blob = new TargetReader (target.ReadMemory (
				field_info, field_count * metadata.FieldInfoSize));

			field_offsets = new int [field_count];
			field_types = new TargetType [field_count];

			for (int i = 0; i < field_count; i++) {
				int offset = i * metadata.FieldInfoSize;

				TargetAddress type_addr = field_blob.PeekAddress (
					offset + metadata.FieldInfoTypeOffset);
				field_types [i] = MonoRuntime.ReadType (
					SymbolFile.MonoLanguage, target, type_addr);
				field_offsets [i] = field_blob.PeekInteger (
					offset + metadata.FieldInfoOffsetOffset);
			}
		}

		public override TargetObject GetField (TargetMemoryAccess target,
						       TargetStructObject instance,
						       TargetFieldInfo field)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!Type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			if (field_loc.HasAddress && field_loc.GetAddress (target).IsNull)
				return null;

			return type.GetObject (target, field_loc);
		}

		public override void SetField (TargetAccess target, TargetStructObject instance,
					       TargetFieldInfo field, TargetObject value)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			if (!Type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			type.SetObject (target, field_loc, value);
		}

		public override TargetObject GetStaticField (Thread target, TargetFieldInfo field)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetAddress data_address = target.CallMethod (
				SymbolFile.MonoLanguage.MonoDebuggerInfo.ClassGetStaticFieldData,
				KlassAddress, 0);

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			return type.GetObject (target, field_loc);
		}

		public override void SetStaticField (Thread target, TargetFieldInfo field,
						     TargetObject obj)
		{
			get_field_offsets (target);

			int offset = field_offsets [field.Position];
			TargetType type = field_types [field.Position];

			TargetAddress data_address = target.CallMethod (
				SymbolFile.MonoLanguage.MonoDebuggerInfo.ClassGetStaticFieldData,
				KlassAddress, 0);

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

			int address_size = target.TargetMemoryInfo.TargetAddressSize;
			MonoMetadataInfo metadata = SymbolFile.MonoLanguage.MonoMetadataInfo;

			TargetAddress method_info = target.ReadAddress (
				KlassAddress + metadata.KlassMethodsOffset);
			int method_count = target.ReadInteger (
				KlassAddress + metadata.KlassMethodCountOffset);

			TargetBlob blob = target.ReadMemory (method_info, method_count * address_size);

			methods = new Hashtable ();
			TargetReader method_reader = new TargetReader (
				blob.Contents, target.TargetMemoryInfo);
			for (int i = 0; i < method_count; i++) {
				TargetAddress address = method_reader.ReadAddress ();

				int mtoken = target.ReadInteger (address + 4);
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
			if (!parent_klass.IsNull)
				return;

			MonoMetadataInfo metadata = SymbolFile.MonoLanguage.MonoMetadataInfo;
			parent_klass = target.ReadAddress (
				KlassAddress + metadata.KlassParentOffset);

			parent_info = ReadClassInfo (SymbolFile.MonoLanguage, target, parent_klass);
		}

		public override bool HasParent {
			get { return type.HasParent; }
		}

		public override TargetClass GetParent (TargetMemoryAccess target)
		{
			get_parent (target);
			return parent_info;
		}
	}
}
