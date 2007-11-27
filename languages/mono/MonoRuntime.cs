using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
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

	internal static class MonoRuntime
	{
		public static TargetType ReadMonoClass (MonoLanguageBackend mono,
							TargetMemoryAccess target,
							TargetAddress address)
		{
			int byval_offset = mono.MonoMetadataInfo.KlassByValArgOffset;
			return ReadType (mono, target, address + byval_offset);
		}

		public static TargetType ReadType (MonoLanguageBackend mono, TargetMemoryAccess memory,
						   TargetAddress address)
		{
			TargetAddress data = memory.ReadAddress (address);
			uint flags = (uint) memory.ReadInteger (
				address + memory.TargetMemoryInfo.TargetAddressSize);

			MonoTypeEnum type = (MonoTypeEnum) ((flags & 0x00ff0000) >> 16);
			bool byref = (int) ((flags & 0x40000000) >> 30) != 0;

			// int attrs = (int) (flags & 0x0000ffff);
			// int num_mods = (int) ((flags & 0x3f000000) >> 24);
			// int pinned = (int) ((flags & 0x80000000) >> 31);

			TargetType target_type = ReadType (mono, memory, type, data);
			if (target_type == null)
				return null;

			if (byref)
				target_type = new MonoPointerType (target_type);

			return target_type;
		}

		static TargetType ReadType (MonoLanguageBackend mono, TargetMemoryAccess memory,
					    MonoTypeEnum type, TargetAddress data)
		{
			switch (type) {
			case MonoTypeEnum.MONO_TYPE_BOOLEAN:
				return mono.BuiltinTypes.BooleanType;
			case MonoTypeEnum.MONO_TYPE_CHAR:
				return mono.BuiltinTypes.CharType;
			case MonoTypeEnum.MONO_TYPE_I1:
				return mono.BuiltinTypes.SByteType;
			case MonoTypeEnum.MONO_TYPE_U1:
				return mono.BuiltinTypes.ByteType;
			case MonoTypeEnum.MONO_TYPE_I2:
				return mono.BuiltinTypes.Int16Type;
			case MonoTypeEnum.MONO_TYPE_U2:
				return mono.BuiltinTypes.UInt16Type;
			case MonoTypeEnum.MONO_TYPE_I4:
				return mono.BuiltinTypes.Int32Type;
			case MonoTypeEnum.MONO_TYPE_U4:
				return mono.BuiltinTypes.UInt32Type;
			case MonoTypeEnum.MONO_TYPE_I8:
				return mono.BuiltinTypes.Int64Type;
			case MonoTypeEnum.MONO_TYPE_U8:
				return mono.BuiltinTypes.UInt64Type;
			case MonoTypeEnum.MONO_TYPE_R4:
				return mono.BuiltinTypes.SingleType;
			case MonoTypeEnum.MONO_TYPE_R8:
				return mono.BuiltinTypes.DoubleType;
			case MonoTypeEnum.MONO_TYPE_STRING:
				return mono.BuiltinTypes.StringType;
			case MonoTypeEnum.MONO_TYPE_OBJECT:
				return mono.BuiltinTypes.ObjectType;
			case MonoTypeEnum.MONO_TYPE_I:
				return mono.BuiltinTypes.IntType;
			case MonoTypeEnum.MONO_TYPE_U:
				return mono.BuiltinTypes.UIntType;

			case MonoTypeEnum.MONO_TYPE_PTR: {
				TargetType target_type = ReadType (mono, memory, data);
				return new MonoPointerType (target_type);
			}

			case MonoTypeEnum.MONO_TYPE_VALUETYPE:
			case MonoTypeEnum.MONO_TYPE_CLASS:
				return mono.ReadMonoClass (memory, data);

			case MonoTypeEnum.MONO_TYPE_SZARRAY: {
				TargetType etype = ReadMonoClass (mono, memory, data);
				return new MonoArrayType (etype, 1);
			}

			case MonoTypeEnum.MONO_TYPE_ARRAY: {
				TargetReader reader = new TargetReader (memory.ReadMemory (
					data, 4 * memory.TargetMemoryInfo.TargetAddressSize));
				TargetAddress klass = reader.ReadAddress ();
				int rank = reader.ReadByte ();
				int numsizes = reader.ReadByte ();
				int numlobounds = reader.ReadByte ();

				if ((numsizes != 0) || (numlobounds != 0))
					throw new InternalError ();

				TargetType etype = ReadMonoClass (mono, memory, klass);
				return new MonoArrayType (etype, rank);
			}

			default:
				Report.Error ("UNKNOWN TYPE: {0}", type);
				return null;
			}
		}

		public static MonoFunctionType ReadMonoMethod (MonoLanguageBackend mono,
							       TargetMemoryAccess memory,
							       TargetAddress address)
		{
			MonoMetadataInfo info = mono.MonoMetadataInfo;

			int token = memory.ReadInteger (address + info.MonoMethodTokenOffset);
			TargetAddress klass = memory.ReadAddress (address + info.MonoMethodKlassOffset);
			TargetAddress image = memory.ReadAddress (klass + info.KlassImageOffset);

			MonoSymbolFile file = mono.GetImage (image);
			if (file == null)
				return null;

			return file.GetFunctionByToken (token);
		}

		//
		// Fundamental types
		//

		public static TargetAddress GetBooleanClass (MonoLanguageBackend mono,
							     TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsBooleanOffset);
		}

		public static TargetAddress GetCharClass (MonoLanguageBackend mono,
							  TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsCharOffset);
		}

		public static TargetAddress GetSByteClass (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsSByteOffset);
		}

		public static TargetAddress GetByteClass (MonoLanguageBackend mono,
							  TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsByteOffset);
		}

		public static TargetAddress GetInt16Class (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsInt16Offset);
		}

		public static TargetAddress GetUInt16Class (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsUInt16Offset);
		}

		public static TargetAddress GetInt32Class (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsInt32Offset);
		}

		public static TargetAddress GetUInt32Class (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsUInt32Offset);
		}

		public static TargetAddress GetInt64Class (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsInt64Offset);
		}

		public static TargetAddress GetUInt64Class (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsUInt64Offset);
		}

		public static TargetAddress GetSingleClass (MonoLanguageBackend mono,
							    TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsSingleOffset);
		}

		public static TargetAddress GetDoubleClass (MonoLanguageBackend mono,
							    TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsDoubleOffset);
		}

		public static TargetAddress GetIntPtrClass (MonoLanguageBackend mono,
							    TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsIntOffset);
		}

		public static TargetAddress GetUIntPtrClass (MonoLanguageBackend mono,
							     TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsUIntOffset);
		}

		public static TargetAddress GetVoidClass (MonoLanguageBackend mono,
							  TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsVoidOffset);
		}

		public static TargetAddress GetStringClass (MonoLanguageBackend mono,
							    TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsStringOffset);
		}

		public static TargetAddress GetObjectClass (MonoLanguageBackend mono,
							    TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsObjectOffset);
		}

		public static TargetAddress GetArrayClass (MonoLanguageBackend mono,
							   TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsArrayOffset);
		}

		public static TargetAddress GetDelegateClass (MonoLanguageBackend mono,
							      TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsDelegateOffset);
		}

		public static TargetAddress GetExceptionClass (MonoLanguageBackend mono,
							       TargetMemoryAccess memory)
		{
			return memory.ReadAddress (mono.MonoMetadataInfo.MonoDefaultsAddress +
						   mono.MonoMetadataInfo.MonoDefaultsExceptionOffset);
		}
	}
}
