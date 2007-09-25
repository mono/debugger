using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : DebuggerMarshalByRefObject
	{
		public readonly int Token;
		public readonly TargetAddress KlassAddress;
		public readonly TargetAddress ParentClass;
		public readonly TargetAddress GenericContainer;
		public readonly TargetAddress GenericClass;

		public readonly Cecil.TypeDefinition Type;

		protected readonly int[] field_offsets;
		protected readonly Hashtable methods;

		internal MonoClassInfo (MonoLanguageBackend mono, TargetMemoryAccess target,
					TargetAddress klass_address)
		{
			this.KlassAddress = klass_address;

			MonoMetadataInfo info = mono.MonoMetadataInfo;

			TargetReader reader = new TargetReader (
				target.ReadMemory (klass_address, info.KlassSize));

			TargetAddress image = reader.ReadAddress ();
			MonoSymbolFile file = mono.GetImage (image);
			if (file == null)
				throw new InternalError ();

			reader.ReadAddress ();
			TargetAddress element_class = reader.ReadAddress ();

			Token = reader.PeekInteger (info.KlassTokenOffset);
			if ((Token & 0xff000000) != 0x02000000)
				throw new InternalError ();

			Type = (Cecil.TypeDefinition) file.ModuleDefinition.LookupByToken (
				Cecil.Metadata.TokenType.TypeDef, Token & 0x00ffffff);
			if (Type == null)
				throw new InternalError ();

			reader.Offset = info.KlassByValArgOffset;
			TargetAddress byval_data_addr = reader.ReadAddress ();
			reader.Offset += 2;
			int type = reader.ReadByte ();

			GenericClass = reader.PeekAddress (info.KlassGenericClassOffset);
			GenericContainer = reader.PeekAddress (info.KlassGenericContainerOffset);
			ParentClass = reader.PeekAddress (info.KlassParentOffset);

			TargetAddress field_info = reader.PeekAddress (info.KlassFieldOffset);
			int field_count = reader.PeekInteger (info.KlassFieldCountOffset);

			TargetBinaryReader field_blob = target.ReadMemory (
				field_info, field_count * info.FieldInfoSize).GetReader ();

			field_offsets = new int [field_count];
			for (int i = 0; i < field_count; i++) {
				field_blob.Position = i * info.FieldInfoSize +
					2 * target.TargetInfo.TargetAddressSize;
				field_offsets [i] = field_blob.ReadInt32 ();
			}

			TargetAddress method_info = reader.PeekAddress (info.KlassMethodsOffset);
			int method_count = reader.PeekInteger (info.KlassMethodCountOffset);

			TargetBlob blob = target.ReadMemory (
				method_info, method_count * target.TargetInfo.TargetAddressSize);

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

		public bool IsGenericClass {
			get { return !GenericClass.IsNull; }
		}

		public int[] FieldOffsets {
			get { return field_offsets; }
		}

		internal TargetAddress GetMethodAddress (int token)
		{
			if (!methods.Contains (token))
				throw new InternalError ();
			return (TargetAddress) methods [token];
		}
	}
}
