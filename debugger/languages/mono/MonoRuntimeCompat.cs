using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoRuntimeCompat : MonoRuntime
	{
		protected readonly MonoDebuggerInfo MonoDebuggerInfo;
		protected readonly MetadataInfo MonoMetadataInfo;

		protected MonoRuntimeCompat (MonoDebuggerInfo info, MetadataInfo metadata)
		{
			this.MonoDebuggerInfo = info;
			this.MonoMetadataInfo = metadata;
		}

		new public static MonoRuntime Create (TargetMemoryAccess memory, MonoDebuggerInfo info)
		{
			MetadataInfo metadata = new MetadataInfo (memory, info.MonoMetadataInfo);
			return new MonoRuntimeCompat (info, metadata);
		}

		//
		// MonoClass
		//

		public override TargetAddress MonoClassGetMonoImage (TargetMemoryAccess memory,
								     TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassImageOffset);
		}

		public override int MonoClassGetToken (TargetMemoryAccess memory,
						       TargetAddress klass)
		{
			return memory.ReadInteger (klass + MonoMetadataInfo.KlassTokenOffset);
		}

		public override TargetAddress MonoClassGetParent (TargetMemoryAccess memory,
								  TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassParentOffset);
		}

		public override TargetAddress MonoClassGetGenericClass (TargetMemoryAccess memory,
									TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassGenericClassOffset);
		}

		public override TargetAddress MonoClassGetGenericContainer (TargetMemoryAccess memory,
									    TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassGenericContainerOffset);
		}

		public override TargetAddress MonoClassGetByValType (TargetMemoryAccess memory,
								     TargetAddress klass)
		{
			return klass + MonoMetadataInfo.KlassByValArgOffset;
		}

		public override int MonoClassGetFieldCount (TargetMemoryAccess memory, TargetAddress klass)
		{
			return memory.ReadInteger (klass + MonoMetadataInfo.KlassFieldCountOffset);
		}

		public override TargetAddress MonoClassGetFieldType (TargetMemoryAccess memory,
								     TargetAddress klass, int index)
		{
			int offset = index * MonoMetadataInfo.FieldInfoSize +
				MonoMetadataInfo.FieldInfoTypeOffset;

			TargetAddress fields = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassFieldOffset);

			return memory.ReadAddress (fields + offset);
		}

		public override int MonoClassGetFieldOffset (TargetMemoryAccess memory,
							     TargetAddress klass, int index)
		{
			int offset = index * MonoMetadataInfo.FieldInfoSize +
				MonoMetadataInfo.FieldInfoOffsetOffset;

			TargetAddress fields = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassFieldOffset);

			return memory.ReadInteger (fields + offset);
		}

		public override int MonoClassGetMethodCount (TargetMemoryAccess memory,
							     TargetAddress klass)
		{
			return memory.ReadInteger (klass + MonoMetadataInfo.KlassMethodCountOffset);
		}

		public override TargetAddress MonoClassGetMethod (TargetMemoryAccess memory,
								  TargetAddress klass, int index)
		{
			TargetAddress methods = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassMethodsOffset);

			methods += index * memory.TargetAddressSize;
			return memory.ReadAddress (methods);
		}

		//
		// MonoMethod
		//

		public override int MonoMethodGetToken (TargetMemoryAccess memory,
							TargetAddress method)
		{
			return memory.ReadInteger (method + MonoMetadataInfo.MonoMethodTokenOffset);
		}

		public override TargetAddress MonoMethodGetClass (TargetMemoryAccess memory,
								  TargetAddress method)
		{
			return memory.ReadAddress (method + MonoMetadataInfo.MonoMethodKlassOffset);
		}

		//
		// MonoType
		//

		public override MonoTypeEnum MonoTypeGetType (TargetMemoryAccess memory,
							      TargetAddress type)
		{
			uint flags = (uint) memory.ReadInteger (
				type + memory.TargetMemoryInfo.TargetAddressSize);

			return (MonoTypeEnum) ((flags & 0x00ff0000) >> 16);
		}

		public override bool MonoTypeGetIsByRef (TargetMemoryAccess memory,
							 TargetAddress type)
		{
			uint flags = (uint) memory.ReadInteger (
				type + memory.TargetMemoryInfo.TargetAddressSize);
			return (int) ((flags & 0x40000000) >> 30) != 0;
		}

		public override TargetAddress MonoTypeGetData (TargetMemoryAccess memory,
							       TargetAddress type)
		{
			return memory.ReadAddress (type);
		}

		public override TargetAddress MonoArrayTypeGetClass (TargetMemoryAccess memory,
								     TargetAddress atype)
		{
			return memory.ReadAddress (atype);
		}

		public override int MonoArrayTypeGetRank (TargetMemoryAccess memory,
							  TargetAddress atype)
		{
			return memory.ReadByte (atype + memory.TargetAddressSize);
		}

		public override int MonoArrayTypeGetNumSizes (TargetMemoryAccess memory,
							      TargetAddress atype)
		{
			return memory.ReadByte (atype + memory.TargetAddressSize + 1);
		}

		public override int MonoArrayTypeGetNumLoBounds (TargetMemoryAccess memory,
								 TargetAddress atype)
		{
			return memory.ReadByte (atype + memory.TargetAddressSize + 2);
		}

		//
		// Fundamental types
		//

		public override TargetAddress GetBooleanClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsBooleanOffset);
		}

		public override TargetAddress GetCharClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsCharOffset);
		}

		public override TargetAddress GetSByteClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsSByteOffset);
		}

		public override TargetAddress GetByteClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsByteOffset);
		}

		public override TargetAddress GetInt16Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsInt16Offset);
		}

		public override TargetAddress GetUInt16Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUInt16Offset);
		}

		public override TargetAddress GetInt32Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsInt32Offset);
		}

		public override TargetAddress GetUInt32Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUInt32Offset);
		}

		public override TargetAddress GetInt64Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsInt64Offset);
		}

		public override TargetAddress GetUInt64Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUInt64Offset);
		}

		public override TargetAddress GetSingleClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsSingleOffset);
		}

		public override TargetAddress GetDoubleClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsDoubleOffset);
		}

		public override TargetAddress GetIntPtrClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsIntOffset);
		}

		public override TargetAddress GetUIntPtrClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUIntOffset);
		}

		public override TargetAddress GetVoidClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsVoidOffset);
		}

		public override TargetAddress GetStringClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsStringOffset);
		}

		public override TargetAddress GetObjectClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsObjectOffset);
		}

		public override TargetAddress GetArrayClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsArrayOffset);
		}

		public override TargetAddress GetDelegateClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsDelegateOffset);
		}

		public override TargetAddress GetExceptionClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsExceptionOffset);
		}

		protected class MetadataInfo
		{
			public readonly int MonoDefaultsSize;
			public readonly TargetAddress MonoDefaultsAddress;
			public readonly int TypeSize;
			public readonly int ArrayTypeSize;
			public readonly int KlassSize;
			public readonly int ThreadSize;

			public readonly int ThreadTidOffset;
			public readonly int ThreadStackPtrOffset;
			public readonly int ThreadEndStackOffset;

			public readonly int KlassImageOffset;
			public readonly int KlassInstanceSizeOffset;
			public readonly int KlassParentOffset;
			public readonly int KlassTokenOffset;
			public readonly int KlassFieldOffset;
			public readonly int KlassFieldCountOffset;
			public readonly int KlassMethodsOffset;
			public readonly int KlassMethodCountOffset;
			public readonly int KlassThisArgOffset;
			public readonly int KlassByValArgOffset;
			public readonly int KlassGenericClassOffset;
			public readonly int KlassGenericContainerOffset;
			public readonly int KlassVTableOffset;
			public readonly int FieldInfoSize;
			public readonly int FieldInfoTypeOffset;
			public readonly int FieldInfoOffsetOffset;

			public readonly int MonoDefaultsCorlibOffset;
			public readonly int MonoDefaultsObjectOffset;
			public readonly int MonoDefaultsByteOffset;
			public readonly int MonoDefaultsVoidOffset;
			public readonly int MonoDefaultsBooleanOffset;
			public readonly int MonoDefaultsSByteOffset;
			public readonly int MonoDefaultsInt16Offset;
			public readonly int MonoDefaultsUInt16Offset;
			public readonly int MonoDefaultsInt32Offset;
			public readonly int MonoDefaultsUInt32Offset;
			public readonly int MonoDefaultsIntOffset;
			public readonly int MonoDefaultsUIntOffset;
			public readonly int MonoDefaultsInt64Offset;
			public readonly int MonoDefaultsUInt64Offset;
			public readonly int MonoDefaultsSingleOffset;
			public readonly int MonoDefaultsDoubleOffset;
			public readonly int MonoDefaultsCharOffset;
			public readonly int MonoDefaultsStringOffset;
			public readonly int MonoDefaultsEnumOffset;
			public readonly int MonoDefaultsArrayOffset;
			public readonly int MonoDefaultsDelegateOffset;
			public readonly int MonoDefaultsExceptionOffset;

			public readonly int MonoMethodKlassOffset;
			public readonly int MonoMethodTokenOffset;
			public readonly int MonoMethodFlagsOffset;
			public readonly int MonoMethodInflatedOffset;

			public readonly int MonoVTableKlassOffset;
			public readonly int MonoVTableVTableOffset;

			public MetadataInfo (TargetMemoryAccess memory, TargetAddress address)
			{
				int size = memory.ReadInteger (address);
				TargetBinaryReader reader = memory.ReadMemory (address, size).GetReader ();
				reader.ReadInt32 ();

				MonoDefaultsSize = reader.ReadInt32 ();
				MonoDefaultsAddress = new TargetAddress (
					memory.AddressDomain, reader.ReadAddress ());

				TypeSize = reader.ReadInt32 ();
				ArrayTypeSize = reader.ReadInt32 ();
				KlassSize = reader.ReadInt32 ();
				ThreadSize = reader.ReadInt32 ();

				ThreadTidOffset = reader.ReadInt32 ();
				ThreadStackPtrOffset = reader.ReadInt32 ();
				ThreadEndStackOffset = reader.ReadInt32 ();

				KlassImageOffset = reader.ReadInt32 ();
				KlassInstanceSizeOffset = reader.ReadInt32 ();
				KlassParentOffset = reader.ReadInt32 ();
				KlassTokenOffset = reader.ReadInt32 ();
				KlassFieldOffset = reader.ReadInt32 ();
				KlassMethodsOffset = reader.ReadInt32 ();
				KlassMethodCountOffset = reader.ReadInt32 ();
				KlassThisArgOffset = reader.ReadInt32 ();
				KlassByValArgOffset = reader.ReadInt32 ();
				KlassGenericClassOffset = reader.ReadInt32 ();
				KlassGenericContainerOffset = reader.ReadInt32 ();
				KlassVTableOffset = reader.ReadInt32 ();

				FieldInfoSize = reader.ReadInt32 ();
				FieldInfoTypeOffset = reader.ReadInt32 ();
				FieldInfoOffsetOffset = reader.ReadInt32 ();

				KlassFieldCountOffset = KlassMethodCountOffset - 8;

				MonoDefaultsCorlibOffset = reader.ReadInt32 ();
				MonoDefaultsObjectOffset = reader.ReadInt32 ();
				MonoDefaultsByteOffset = reader.ReadInt32 ();
				MonoDefaultsVoidOffset = reader.ReadInt32 ();
				MonoDefaultsBooleanOffset = reader.ReadInt32 ();
				MonoDefaultsSByteOffset = reader.ReadInt32 ();
				MonoDefaultsInt16Offset = reader.ReadInt32 ();
				MonoDefaultsUInt16Offset = reader.ReadInt32 ();
				MonoDefaultsInt32Offset = reader.ReadInt32 ();
				MonoDefaultsUInt32Offset = reader.ReadInt32 ();
				MonoDefaultsIntOffset = reader.ReadInt32 ();
				MonoDefaultsUIntOffset = reader.ReadInt32 ();
				MonoDefaultsInt64Offset = reader.ReadInt32 ();
				MonoDefaultsUInt64Offset = reader.ReadInt32 ();
				MonoDefaultsSingleOffset = reader.ReadInt32 ();
				MonoDefaultsDoubleOffset = reader.ReadInt32 ();
				MonoDefaultsCharOffset = reader.ReadInt32 ();
				MonoDefaultsStringOffset = reader.ReadInt32 ();
				MonoDefaultsEnumOffset = reader.ReadInt32 ();
				MonoDefaultsArrayOffset = reader.ReadInt32 ();
				MonoDefaultsDelegateOffset = reader.ReadInt32 ();
				MonoDefaultsExceptionOffset = reader.ReadInt32 ();

				MonoMethodKlassOffset = reader.ReadInt32 ();
				MonoMethodTokenOffset = reader.ReadInt32 ();
				MonoMethodFlagsOffset = reader.ReadInt32 ();
				MonoMethodInflatedOffset = reader.ReadInt32 ();

				MonoVTableKlassOffset = reader.ReadInt32 ();
				MonoVTableVTableOffset = reader.ReadInt32 ();
			}
		}
	}
}
