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

			int address_size = target.TargetInfo.TargetAddressSize;
			MonoMetadataInfo info = file.MonoLanguage.MonoMetadataInfo;

			reader.Offset = info.KlassByValArgOffset;
			TargetAddress byval_data_addr = reader.ReadAddress ();
			reader.Offset += 2;
			int type = reader.ReadByte ();

			GenericClass = reader.PeekAddress (info.KlassGenericClassOffset);
			GenericContainer = reader.PeekAddress (info.KlassGenericContainerOffset);
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

			int address_size = target.TargetInfo.TargetAddressSize;
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

		public int[] GetFieldOffsets (TargetMemoryAccess target)
		{
			get_field_offsets (target);
			return field_offsets;
		}

		public TargetType[] GetFieldTypes (TargetMemoryAccess target)
		{
			get_field_offsets (target);
			return field_types;
		}

		void get_methods (TargetMemoryAccess target)
		{
			if (methods != null)
				return;

			int address_size = target.TargetInfo.TargetAddressSize;
			MonoMetadataInfo metadata = SymbolFile.MonoLanguage.MonoMetadataInfo;

			TargetAddress method_info = target.ReadAddress (
				KlassAddress + metadata.KlassMethodsOffset);
			int method_count = target.ReadInteger (
				KlassAddress + metadata.KlassMethodCountOffset);

			TargetBlob blob = target.ReadMemory (method_info, method_count * address_size);

			methods = new Hashtable ();
			TargetReader method_reader = new TargetReader (blob.Contents, target.TargetInfo);
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

		public MonoClassInfo GetParent (TargetMemoryAccess target)
		{
			get_parent (target);
			return parent_info;
		}
	}
}
