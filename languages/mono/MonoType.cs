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

	internal static class MonoType
	{
		public static TargetType Read (MonoLanguageBackend mono, TargetMemoryAccess memory,
					       TargetAddress address)
		{
			TargetAddress data = memory.ReadAddress (address);
			uint flags = (uint) memory.ReadInteger (address + memory.TargetInfo.TargetAddressSize);

			Console.WriteLine ("READ TYPE: {0} {1} {2:x}", address, data, flags);

			int attrs = (int) (flags & 0x0000ffff);
			MonoTypeEnum type = (MonoTypeEnum) ((flags & 0x00ff0000) >> 16);
			int num_mods = (int) ((flags & 0x3f000000) >> 24);
			int byref = (int) ((flags & 0x40000000) >> 30);
			int pinned = (int) ((flags & 0x80000000) >> 31);

			Console.WriteLine ("READ TYPE #1: {0} {1} {2} {3} {4}",
					   attrs, type, num_mods, byref, pinned);

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

			case MonoTypeEnum.MONO_TYPE_VALUETYPE:
			case MonoTypeEnum.MONO_TYPE_CLASS:
				return mono.GetClass (memory, data);

			case MonoTypeEnum.MONO_TYPE_SZARRAY: {
				TargetType element_type = mono.GetClass (memory, data);
				return new MonoArrayType (element_type, 1);
			}

			case MonoTypeEnum.MONO_TYPE_ARRAY: {
				TargetReader reader = new TargetReader (memory.ReadMemory (
					data, 4 * memory.TargetInfo.TargetAddressSize));
				TargetType element_type = mono.GetClass (memory, reader.ReadAddress ());
				int rank = reader.ReadByte ();
				int numsizes = reader.ReadByte ();
				int numlobounds = reader.ReadByte ();

				if ((numsizes != 0) || (numlobounds != 0))
					throw new InternalError ();

				return new MonoArrayType (element_type, rank);
			}

			case MonoTypeEnum.MONO_TYPE_GENERICINST: {
				TargetAddress ptr = data;

				TargetAddress container_addr = memory.ReadAddress (ptr);
				ptr += memory.TargetInfo.TargetAddressSize;

				MonoGenericContext context = MonoGenericContext.ReadGenericContext (
					mono, memory, ptr);

				ptr += 3 * memory.TargetInfo.TargetAddressSize;

				TargetAddress cached = memory.ReadAddress (ptr);

				TargetType container_type = mono.GetClass (memory, container_addr);

				Console.WriteLine ("GENERIC CLASS: {0} {1} {2} - {3}",
						   container_addr, container_type, context, cached);

				if (!cached.IsNull) {
					TargetType klass = mono.GetClass (memory, cached);
					Console.WriteLine ("GENERIC CLASS #1: {0}", klass);
					return klass;
				}

				return null;
			}

			default:
				Report.Error ("UNKNOWN TYPE: {0}", type);
				return null;
			}
		}

		public static TargetType ReadGenericClass (MonoLanguageBackend mono,
							   TargetMemoryAccess memory,
							   TargetAddress address)
		{
			int addr_size = memory.TargetInfo.TargetAddressSize;
			TargetAddress container_addr = memory.ReadAddress (address);

			MonoGenericContext context = MonoGenericContext.ReadGenericContext (
				mono, memory, address + addr_size);

			TargetAddress cached = memory.ReadAddress (address + 4 * addr_size);

			Console.WriteLine ("READ GENERIC CLASS: {0} {1} {2}",
					   container_addr, context, cached);

			return null;

			if (!cached.IsNull) {
				TargetType klass = mono.GetClass (memory, cached);
				Console.WriteLine ("READ GENERIC CLASS #1: {0}", klass);
				return klass;
			}

			return null;
		}
	}
}
