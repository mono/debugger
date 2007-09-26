using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : DebuggerMarshalByRefObject
	{
		public readonly MonoSymbolFile SymbolFile;
		public readonly TargetAddress KlassAddress;
		public readonly TargetAddress ParentClass;
		public readonly TargetAddress GenericContainer;
		public readonly TargetAddress GenericClass;

		public readonly Cecil.TypeDefinition CecilType;
		public readonly MonoClassType ClassType;
		public readonly TargetType Type;

		int[] field_offsets;
		Hashtable methods;

		public static MonoClassInfo ReadClassInfo (MonoLanguageBackend mono,
							   TargetMemoryAccess target,
							   TargetAddress klass_address)
		{
			TargetReader reader = new TargetReader (
				target.ReadMemory (klass_address, mono.MonoMetadataInfo.KlassSize));

			TargetAddress image = reader.ReadAddress ();
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

			return new MonoClassInfo (file, typedef, target, reader, klass_address);
		}

		public static MonoClassInfo ReadClassInfo (MonoSymbolFile file,
							   Cecil.TypeDefinition typedef,
							   TargetMemoryAccess target,
							   TargetAddress klass_address)
		{
			MonoMetadataInfo info = file.MonoLanguage.MonoMetadataInfo;

			TargetReader reader = new TargetReader (
				target.ReadMemory (klass_address, info.KlassSize));

			return new MonoClassInfo (file, typedef, target, reader, klass_address);
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

			TargetAddress element_class = reader.PeekAddress (2 * address_size);

			reader.Offset = info.KlassByValArgOffset;
			TargetAddress byval_data_addr = reader.ReadAddress ();
			reader.Offset += 2;
			int type = reader.ReadByte ();

			ClassType = new MonoClassType (file, typedef, this);

			GenericClass = reader.PeekAddress (info.KlassGenericClassOffset);
			GenericContainer = reader.PeekAddress (info.KlassGenericContainerOffset);
			ParentClass = reader.PeekAddress (info.KlassParentOffset);
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

			TargetBinaryReader field_blob = target.ReadMemory (
				field_info, field_count * metadata.FieldInfoSize).GetReader ();

			field_offsets = new int [field_count];
			for (int i = 0; i < field_count; i++) {
				field_blob.Position = i * metadata.FieldInfoSize + 2 * address_size;
				field_offsets [i] = field_blob.ReadInt32 ();
			}
		}

		public int[] GetFieldOffsets (TargetMemoryAccess target)
		{
			get_field_offsets (target);
			return field_offsets;
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
	}
}
