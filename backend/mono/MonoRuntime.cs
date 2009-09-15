using System;

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend.Mono
{
	internal enum MonoTypeEnum
	{
		MONO_TYPE_END        = 0x00,       /* End of List */
		MONO_TYPE_VOID       = 0x01,
		MONO_TYPE_BOOLEAN    = 0x02,
		MONO_TYPE_CHAR       = 0x03,
		MONO_TYPE_I1         = 0x04,
		MONO_TYPE_U1         = 0x05,
		MONO_TYPE_I2         = 0x06,
		MONO_TYPE_U2         = 0x07,
		MONO_TYPE_I4         = 0x08,
		MONO_TYPE_U4         = 0x09,
		MONO_TYPE_I8         = 0x0a,
		MONO_TYPE_U8         = 0x0b,
		MONO_TYPE_R4         = 0x0c,
		MONO_TYPE_R8         = 0x0d,
		MONO_TYPE_STRING     = 0x0e,
		MONO_TYPE_PTR        = 0x0f,       /* arg: <type> token */
		MONO_TYPE_BYREF      = 0x10,       /* arg: <type> token */
		MONO_TYPE_VALUETYPE  = 0x11,       /* arg: <type> token */
		MONO_TYPE_CLASS      = 0x12,       /* arg: <type> token */
		MONO_TYPE_VAR	     = 0x13,	   /* number */
		MONO_TYPE_ARRAY      = 0x14,       /* type, rank, boundsCount, bound1, loCount, lo1 */
		MONO_TYPE_GENERICINST= 0x15,	   /* <type> <type-arg-count> <type-1> \x{2026} <type-n> */
		MONO_TYPE_TYPEDBYREF = 0x16,
		MONO_TYPE_I          = 0x18,
		MONO_TYPE_U          = 0x19,
		MONO_TYPE_FNPTR      = 0x1b,	      /* arg: full method signature */
		MONO_TYPE_OBJECT     = 0x1c,
		MONO_TYPE_SZARRAY    = 0x1d,       /* 0-based one-dim-array */
		MONO_TYPE_MVAR	     = 0x1e,       /* number */
		MONO_TYPE_CMOD_REQD  = 0x1f,       /* arg: typedef or typeref token */
		MONO_TYPE_CMOD_OPT   = 0x20,       /* optional arg: typedef or typref token */
		MONO_TYPE_INTERNAL   = 0x21,       /* CLR internal type */

		MONO_TYPE_MODIFIER   = 0x40,       /* Or with the following types */
		MONO_TYPE_SENTINEL   = 0x41,       /* Sentinel for varargs method signature */
		MONO_TYPE_PINNED     = 0x45,       /* Local var that points to pinned object */

		MONO_TYPE_ENUM       = 0x55        /* an enumeration */
	}

	internal class MonoRuntime : DebuggerMarshalByRefObject
	{
		protected readonly MonoDebuggerInfo MonoDebuggerInfo;
		protected readonly MetadataInfo MonoMetadataInfo;

		protected MonoRuntime (MonoDebuggerInfo info, MetadataInfo metadata)
		{
			this.MonoDebuggerInfo = info;
			this.MonoMetadataInfo = metadata;
		}

		public static MonoRuntime Create (TargetMemoryAccess memory, MonoDebuggerInfo info)
		{
			MetadataInfo metadata = new MetadataInfo (memory, info.MonoMetadataInfo);
			return new MonoRuntime (info, metadata);
		}

		//
		// MonoClass
		//

		public TargetAddress MonoClassGetMonoImage (TargetMemoryAccess memory,
							    TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassImageOffset);
		}

		public int MonoClassGetToken (TargetMemoryAccess memory,
					      TargetAddress klass)
		{
			return memory.ReadInteger (klass + MonoMetadataInfo.KlassTokenOffset);
		}

		public int MonoClassGetInstanceSize (TargetMemoryAccess memory,
						     TargetAddress klass)
		{
			int flags = memory.ReadInteger (klass + 4 * memory.TargetAddressSize);

			bool size_inited = (flags & 4) != 0;
			bool valuetype = (flags & 8) != 0;

			if (!size_inited)
				throw new TargetException (TargetError.ClassNotInitialized);

			int size = memory.ReadInteger (klass + 4 + 3 * memory.TargetAddressSize);
			if (valuetype)
				size -= 2 * memory.TargetAddressSize;

			return size;
		}

		public TargetAddress MonoClassGetParent (TargetMemoryAccess memory,
							 TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassParentOffset);
		}

		public TargetAddress MonoClassGetGenericClass (TargetMemoryAccess memory,
							       TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassGenericClassOffset);
		}

		public TargetAddress MonoClassGetGenericContainer (TargetMemoryAccess memory,
								   TargetAddress klass)
		{
			return memory.ReadAddress (klass + MonoMetadataInfo.KlassGenericContainerOffset);
		}

		public TargetAddress MonoClassGetByValType (TargetMemoryAccess memory,
							    TargetAddress klass)
		{
			return klass + MonoMetadataInfo.KlassByValArgOffset;
		}

		public bool MonoClassHasFields (TargetMemoryAccess memory, TargetAddress klass)
		{
			TargetAddress fields = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassFieldOffset);
			return !fields.IsNull;
		}

		public int MonoClassGetFieldCount (TargetMemoryAccess memory, TargetAddress klass)
		{
			return memory.ReadInteger (klass + MonoMetadataInfo.KlassFieldCountOffset);
		}

		public TargetAddress MonoClassGetFieldType (TargetMemoryAccess memory, TargetAddress klass,
							    int index)
		{
			int offset = index * MonoMetadataInfo.FieldInfoSize +
				MonoMetadataInfo.FieldInfoTypeOffset;

			TargetAddress fields = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassFieldOffset);
			if (fields.IsNull)
				throw new TargetException (TargetError.ClassNotInitialized);

			return memory.ReadAddress (fields + offset);
		}

		public int MonoClassGetFieldOffset (TargetMemoryAccess memory, TargetAddress klass,
						    int index)
		{
			int offset = index * MonoMetadataInfo.FieldInfoSize +
				MonoMetadataInfo.FieldInfoOffsetOffset;

			TargetAddress fields = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassFieldOffset);
			if (fields.IsNull)
				throw new TargetException (TargetError.ClassNotInitialized);

			return memory.ReadInteger (fields + offset);
		}

		public bool MonoClassHasMethods (TargetMemoryAccess memory, TargetAddress klass)
		{
			TargetAddress methods = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassMethodsOffset);
			return !methods.IsNull;
		}

		public int MonoClassGetMethodCount (TargetMemoryAccess memory, TargetAddress klass)
		{
			return memory.ReadInteger (klass + MonoMetadataInfo.KlassMethodCountOffset);
		}

		public TargetAddress MonoClassGetMethod (TargetMemoryAccess memory, TargetAddress klass,
							 int index)
		{
			TargetAddress methods = memory.ReadAddress (
				klass + MonoMetadataInfo.KlassMethodsOffset);

			if (methods.IsNull)
				throw new TargetException (TargetError.ClassNotInitialized);

			methods += index * memory.TargetAddressSize;
			return memory.ReadAddress (methods);
		}

		//
		// MonoMethod
		//

		public int MonoMethodGetToken (TargetMemoryAccess memory, TargetAddress method)
		{
			return memory.ReadInteger (method + MonoMetadataInfo.MonoMethodTokenOffset);
		}

		public TargetAddress MonoMethodGetClass (TargetMemoryAccess memory, TargetAddress method)
		{
			return memory.ReadAddress (method + MonoMetadataInfo.MonoMethodKlassOffset);
		}

		//
		// MonoType
		//

		public MonoTypeEnum MonoTypeGetType (TargetMemoryAccess memory, TargetAddress type)
		{
			uint flags = (uint) memory.ReadInteger (
				type + memory.TargetMemoryInfo.TargetAddressSize);

			return (MonoTypeEnum) ((flags & 0x00ff0000) >> 16);
		}

		public bool MonoTypeGetIsByRef (TargetMemoryAccess memory, TargetAddress type)
		{
			uint flags = (uint) memory.ReadInteger (
				type + memory.TargetMemoryInfo.TargetAddressSize);
			return (int) ((flags & 0x40000000) >> 30) != 0;
		}

		public TargetAddress MonoTypeGetData (TargetMemoryAccess memory, TargetAddress type)
		{
			return memory.ReadAddress (type);
		}

		public TargetAddress MonoArrayTypeGetClass (TargetMemoryAccess memory,
							    TargetAddress atype)
		{
			return memory.ReadAddress (atype);
		}

		public int MonoArrayTypeGetRank (TargetMemoryAccess memory,
						 TargetAddress atype)
		{
			return memory.ReadByte (atype + memory.TargetAddressSize);
		}

		public int MonoArrayTypeGetNumSizes (TargetMemoryAccess memory,
						     TargetAddress atype)
		{
			return memory.ReadByte (atype + memory.TargetAddressSize + 1);
		}

		public int MonoArrayTypeGetNumLoBounds (TargetMemoryAccess memory,
							TargetAddress atype)
		{
			return memory.ReadByte (atype + memory.TargetAddressSize + 2);
		}

		internal void MonoArrayTypeGetBounds (TargetMemoryAccess memory,
						      TargetAddress data)
		{
			//
			// FIXME: Only check whether the low bounds are all zero
			//
			int num_sizes = memory.ReadByte (data + memory.TargetAddressSize + 1);
			if (num_sizes != 0)
				throw new InternalError ();

			int num_lobounds = memory.ReadByte (data + memory.TargetAddressSize + 2);
			if (num_lobounds == 0)
				return;

			TargetAddress array = memory.ReadAddress (data + 3 * memory.TargetAddressSize);
			TargetBinaryReader bounds = memory.ReadMemory (array, num_lobounds * 4).GetReader ();
			for (int i = 0; i < num_lobounds; i++) {
				int bound = bounds.ReadInt32 ();
				if (bound != 0)
					throw new InternalError ();
			}
		}

		//
		// Fundamental types
		//

		public TargetAddress GetBooleanClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsBooleanOffset);
		}

		public TargetAddress GetCharClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsCharOffset);
		}

		public TargetAddress GetSByteClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsSByteOffset);
		}

		public TargetAddress GetByteClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsByteOffset);
		}

		public TargetAddress GetInt16Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsInt16Offset);
		}

		public TargetAddress GetUInt16Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUInt16Offset);
		}

		public TargetAddress GetInt32Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsInt32Offset);
		}

		public TargetAddress GetUInt32Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUInt32Offset);
		}

		public TargetAddress GetInt64Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsInt64Offset);
		}

		public TargetAddress GetUInt64Class (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUInt64Offset);
		}

		public TargetAddress GetSingleClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsSingleOffset);
		}

		public TargetAddress GetDoubleClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsDoubleOffset);
		}

		public TargetAddress GetIntPtrClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsIntOffset);
		}

		public TargetAddress GetUIntPtrClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsUIntOffset);
		}

		public TargetAddress GetVoidClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsVoidOffset);
		}

		public TargetAddress GetStringClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsStringOffset);
		}

		public TargetAddress GetObjectClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsObjectOffset);
		}

		public TargetAddress GetArrayClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsArrayOffset);
		}

		public TargetAddress GetDelegateClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsDelegateOffset);
		}

		public TargetAddress GetExceptionClass (TargetMemoryAccess memory)
		{
			return memory.ReadAddress (MonoMetadataInfo.MonoDefaultsAddress +
						   MonoMetadataInfo.MonoDefaultsExceptionOffset);
		}

		public MonoMethodSignature GetMethodSignature (MonoLanguageBackend mono,
							       TargetMemoryAccess memory,
							       TargetAddress signature)
		{
			int count = memory.ReadInteger (signature + 4) & 0x0000ffff;

			int offset = memory.TargetAddressSize == 8 ? 16 : 12;
			TargetAddress ret = memory.ReadAddress (signature + offset);

			TargetType ret_type = mono.ReadType (memory, ret);
			if (count == 0)
				return new MonoMethodSignature (ret_type, new TargetType [0]);

			offset += memory.TargetAddressSize;
			TargetReader reader = new TargetReader (
				memory.ReadMemory (signature + offset, count * memory.TargetAddressSize));

			TargetType[] param_types = new TargetType [count];
			for (int i = 0; i < count; i++)
				param_types [i] = mono.ReadType (memory, reader.ReadAddress ());

			return new MonoMethodSignature (ret_type, param_types);
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

		//
		// The following API is new in `terrania'.
		//

		public GenericClassInfo GetGenericClass (TargetMemoryAccess memory,
							 TargetAddress address)
		{
			int addr_size = memory.TargetMemoryInfo.TargetAddressSize;

			TargetReader reader = new TargetReader (memory.ReadMemory (address, 5 * addr_size));
			TargetAddress container = reader.ReadAddress ();
			TargetAddress class_inst = reader.ReadAddress ();
			reader.ReadAddress (); /* method_inst */
			reader.ReadAddress ();
			TargetAddress cached_class = reader.ReadAddress ();

			int inst_id = memory.ReadInteger (class_inst);
			int inst_data = memory.ReadInteger (class_inst + 4);

			TargetAddress inst_argv;
			if (MonoDebuggerInfo.MajorVersion == 80)
				inst_argv = memory.ReadAddress (class_inst + 8);
			else
				inst_argv = class_inst + 8;

			int type_argc = inst_data & 0x3fffff;

			TargetReader argv_reader = new TargetReader (
				memory.ReadMemory (inst_argv, type_argc * addr_size));

			TargetAddress[] type_args = new TargetAddress [type_argc];
			for (int i = 0; i < type_argc; i++)
				type_args [i] = argv_reader.ReadAddress ();

			TargetAddress cached_class_ptr = address + 4 * addr_size;

			return new GenericClassInfo (container, type_args, cached_class_ptr,
						     cached_class);
		}

		public class GenericClassInfo
		{
			/* `MonoClass *' of the container class. */
			public readonly TargetAddress ContainerClass;

			/* `MonoType *' array of the instantiation. */
			public readonly TargetAddress[] TypeArguments;

			/* `MonoClass *' of this instantiation, if present. */
			public readonly TargetAddress KlassPtr;
			public readonly TargetAddress Klass;

			public GenericClassInfo (TargetAddress container, TargetAddress[] type_args,
						 TargetAddress klass_ptr, TargetAddress klass)
			{
				this.ContainerClass = container;
				this.TypeArguments = type_args;
				this.KlassPtr = klass_ptr;
				this.Klass = klass;
			}
		}

		public GenericParamInfo GetGenericParameter (TargetMemoryAccess memory,
							     TargetAddress address)
		{
			int addr_size = memory.TargetMemoryInfo.TargetAddressSize;

			TargetReader reader = new TargetReader (
				memory.ReadMemory (address, 4 * addr_size + 4));
			TargetAddress container = reader.ReadAddress ();
			TargetAddress klass = reader.ReadAddress ();
			TargetAddress name_addr = reader.ReadAddress ();
			reader.BinaryReader.ReadInt16 (); /* flags */
			int pos = reader.BinaryReader.ReadInt16 ();

			string name;
			if (!name_addr.IsNull)
				name = memory.ReadString (name_addr);
			else
				name = String.Format ("!{0}", pos);

			return new GenericParamInfo (container, klass, name, pos);
		}

		public class GenericParamInfo
		{
			public readonly TargetAddress Container;
			public readonly TargetAddress Klass;
			public readonly string Name;
			public readonly int Position;

			public GenericParamInfo (TargetAddress container, TargetAddress klass,
						 string name, int pos)
			{
				this.Container = container;
				this.Klass = klass;
				this.Name = name;
				this.Position = pos;
			}
		}

		public AppDomainInfo GetAppDomainInfo (MonoLanguageBackend mono, TargetMemoryAccess memory,
						       TargetAddress address)
		{
			int addr_size = memory.TargetMemoryInfo.TargetAddressSize;
			TargetReader reader = new TargetReader (memory.ReadMemory (address, 12 * addr_size));

			return new AppDomainInfo (mono, memory, reader);
		}

		public class AppDomainInfo
		{
			public readonly string ApplicationBase;
			public readonly string ApplicationName;
			public readonly string CachePath;
			public readonly string ConfigFile;
			public readonly string DynamicBase;
			public readonly string ShadowCopyDirectories;
			public readonly bool ShadowCopyFiles;

			public string ShadowCopyPath;

			public AppDomainInfo (MonoLanguageBackend mono, TargetMemoryAccess memory, TargetReader reader)
			{
				int addr_size = memory.TargetMemoryInfo.TargetAddressSize;

				reader.Offset = 2 * addr_size;
				ApplicationBase = MonoStringObject.ReadString (mono, memory, reader.ReadAddress ());
				ApplicationName = MonoStringObject.ReadString (mono, memory, reader.ReadAddress ());
				CachePath = MonoStringObject.ReadString (mono, memory, reader.ReadAddress ());
				ConfigFile = MonoStringObject.ReadString (mono, memory, reader.ReadAddress ());
				DynamicBase = MonoStringObject.ReadString (mono, memory, reader.ReadAddress ());
				reader.Offset += 3 * addr_size;
				ShadowCopyDirectories = MonoStringObject.ReadString (mono, memory, reader.ReadAddress ());
				ShadowCopyFiles = MonoStringObject.ReadString (mono, memory, reader.ReadAddress ()) == "true";
			}

			public override string ToString ()
			{
				return String.Format ("AppDomainInfo ({0}:{1}:{2}:{3}:{4}:{5}:{6})",
						      ApplicationBase, ApplicationName, CachePath, ConfigFile,
						      DynamicBase, ShadowCopyDirectories, ShadowCopyFiles);
			}
		}
	}
}
