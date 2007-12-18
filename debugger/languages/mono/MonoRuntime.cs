using System;

using Mono.Debugger.Backend;

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

	internal abstract class MonoRuntime : DebuggerMarshalByRefObject
	{
		public static MonoRuntime Create (TargetMemoryAccess memory, MonoDebuggerInfo info)
		{
			MonoRuntime runtime = MonoRuntimeCompat.Create (memory, info);
#if HAVE_MONO_DBG_LIBRARY
			runtime = new MonoRuntimeLibrary (runtime);
#endif
			return runtime;
		}

		public abstract TargetAddress MonoClassGetMonoImage (TargetMemoryAccess memory,
								     TargetAddress klass);

		public abstract int MonoClassGetToken (TargetMemoryAccess memory,
						       TargetAddress klass);

		public abstract TargetAddress MonoClassGetParent (TargetMemoryAccess memory,
								  TargetAddress klass);

		public abstract TargetAddress MonoClassGetGenericClass (TargetMemoryAccess memory,
									TargetAddress klass);

		public abstract TargetAddress MonoClassGetGenericContainer (TargetMemoryAccess memory,
									    TargetAddress klass);

		public abstract TargetAddress MonoClassGetByValType (TargetMemoryAccess memory,
								     TargetAddress klass);

		public abstract int MonoClassGetFieldCount (TargetMemoryAccess memory,
							    TargetAddress klass);

		public abstract TargetAddress MonoClassGetFieldType (TargetMemoryAccess memory,
								     TargetAddress klass, int index);

		public abstract int MonoClassGetFieldOffset (TargetMemoryAccess memory,
							     TargetAddress klass, int index);

		public abstract int MonoClassGetMethodCount (TargetMemoryAccess memory,
							     TargetAddress klass);

		public abstract TargetAddress MonoClassGetMethod (TargetMemoryAccess memory,
								  TargetAddress klass, int index);

		//
		// MonoMethod
		//

		public abstract int MonoMethodGetToken (TargetMemoryAccess memory,
							TargetAddress method);

		public abstract TargetAddress MonoMethodGetClass (TargetMemoryAccess memory,
								  TargetAddress method);

		//
		// MonoType
		//

		public abstract MonoTypeEnum MonoTypeGetType (TargetMemoryAccess memory,
							      TargetAddress type);

		public abstract bool MonoTypeGetIsByRef (TargetMemoryAccess memory, TargetAddress type);

		public abstract TargetAddress MonoTypeGetData (TargetMemoryAccess memory,
							       TargetAddress type);

		public abstract TargetAddress MonoArrayTypeGetClass (TargetMemoryAccess memory,
								     TargetAddress atype);

		public abstract int MonoArrayTypeGetRank (TargetMemoryAccess memory,
							  TargetAddress atype);

		public abstract int MonoArrayTypeGetNumSizes (TargetMemoryAccess memory,
							      TargetAddress atype);

		public abstract int MonoArrayTypeGetNumLoBounds (TargetMemoryAccess memory,
								 TargetAddress atype);

		//
		// Fundamental types
		//

		public abstract TargetAddress GetBooleanClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetCharClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetSByteClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetByteClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetInt16Class (TargetMemoryAccess memory);

		public abstract TargetAddress GetUInt16Class (TargetMemoryAccess memory);

		public abstract TargetAddress GetInt32Class (TargetMemoryAccess memory);

		public abstract TargetAddress GetUInt32Class (TargetMemoryAccess memory);

		public abstract TargetAddress GetInt64Class (TargetMemoryAccess memory);

		public abstract TargetAddress GetUInt64Class (TargetMemoryAccess memory);

		public abstract TargetAddress GetSingleClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetDoubleClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetIntPtrClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetUIntPtrClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetVoidClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetStringClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetObjectClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetArrayClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetDelegateClass (TargetMemoryAccess memory);

		public abstract TargetAddress GetExceptionClass (TargetMemoryAccess memory);
	}
}
