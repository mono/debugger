using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal static class OldMonoRuntime
	{
		public static TargetType ReadMonoClass (MonoLanguageBackend mono,
							TargetMemoryAccess target,
							TargetAddress klass)
		{
			TargetAddress byval_type = mono.MonoRuntime.MonoClassGetByValType (target, klass);
			return ReadType (mono, target, byval_type);
		}

		public static TargetType ReadType (MonoLanguageBackend mono, TargetMemoryAccess memory,
						   TargetAddress address)
		{
			TargetAddress data = mono.MonoRuntime.MonoTypeGetData (memory, address);
			MonoTypeEnum type = mono.MonoRuntime.MonoTypeGetType (memory, address);
			bool byref = mono.MonoRuntime.MonoTypeGetIsByRef (memory, address);

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
				TargetAddress klass = mono.MonoRuntime.MonoArrayTypeGetClass (memory, data);
				int rank = mono.MonoRuntime.MonoArrayTypeGetRank (memory, data);

				int numsizes = mono.MonoRuntime.MonoArrayTypeGetNumSizes (memory, data);
				int numlobounds = mono.MonoRuntime.MonoArrayTypeGetNumLoBounds (memory, data);

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
			int token = mono.MonoRuntime.MonoMethodGetToken (memory, address);
			TargetAddress klass = mono.MonoRuntime.MonoMethodGetClass (memory, address);
			TargetAddress image = mono.MonoRuntime.MonoClassGetMonoImage (memory, klass);

			MonoSymbolFile file = mono.GetImage (image);
			if (file == null)
				return null;

			return file.GetFunctionByToken (token);
		}
	}
}
