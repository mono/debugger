#if HAVE_MONO_DBG_LIBRARY

using System;
using System.Runtime.InteropServices;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoRuntimeLibrary : MonoRuntime
	{
		protected readonly MonoRuntime compat;

		[DllImport("mono-dbg")]
		static extern int mono_dbg_get_version ();

		internal MonoRuntimeLibrary (MonoRuntime compat)
		{
			this.compat = compat;

			if (mono_dbg_get_version () < 66)
				throw new InternalError ();
		}

		//
		// MonoClass
		//

		public override TargetAddress MonoClassGetMonoImage (TargetMemoryAccess memory,
								     TargetAddress klass)
		{
			return compat.MonoClassGetMonoImage (memory, klass);
		}

		public override int MonoClassGetToken (TargetMemoryAccess memory,
						       TargetAddress klass)
		{
			return compat.MonoClassGetToken (memory, klass);
		}

		public override TargetAddress MonoClassGetParent (TargetMemoryAccess memory,
								  TargetAddress klass)
		{
			return compat.MonoClassGetParent (memory, klass);
		}

		public override TargetAddress MonoClassGetGenericClass (TargetMemoryAccess memory,
									TargetAddress klass)
		{
			return compat.MonoClassGetGenericClass (memory, klass);
		}

		public override TargetAddress MonoClassGetGenericContainer (TargetMemoryAccess memory,
									    TargetAddress klass)
		{
			return compat.MonoClassGetGenericContainer (memory, klass);
		}

		public override TargetAddress MonoClassGetByValType (TargetMemoryAccess memory,
								     TargetAddress klass)
		{
			return compat.MonoClassGetByValType (memory, klass);
		}

		public override int MonoClassGetFieldCount (TargetMemoryAccess memory, TargetAddress klass)
		{
			return compat.MonoClassGetFieldCount (memory, klass);
		}

		public override TargetAddress MonoClassGetFieldType (TargetMemoryAccess memory,
								     TargetAddress klass, int index)
		{
			return compat.MonoClassGetFieldType (memory, klass, index);
		}

		public override int MonoClassGetFieldOffset (TargetMemoryAccess memory,
							     TargetAddress klass, int index)
		{
			return compat.MonoClassGetFieldOffset (memory, klass, index);
		}

		public override int MonoClassGetMethodCount (TargetMemoryAccess memory,
							     TargetAddress klass)
		{
			return compat.MonoClassGetMethodCount (memory, klass);
		}

		public override TargetAddress MonoClassGetMethod (TargetMemoryAccess memory,
								  TargetAddress klass, int index)
		{
			return compat.MonoClassGetMethod (memory, klass, index);
		}

		//
		// MonoMethod
		//

		public override int MonoMethodGetToken (TargetMemoryAccess memory,
							TargetAddress method)
		{
			return compat.MonoMethodGetToken (memory, method);
		}

		public override TargetAddress MonoMethodGetClass (TargetMemoryAccess memory,
								  TargetAddress method)
		{
			return compat.MonoMethodGetClass (memory, method);
		}

		//
		// MonoType
		//

		public override MonoTypeEnum MonoTypeGetType (TargetMemoryAccess memory,
							      TargetAddress type)
		{
			return compat.MonoTypeGetType (memory, type);
		}

		public override bool MonoTypeGetIsByRef (TargetMemoryAccess memory,
							 TargetAddress type)
		{
			return compat.MonoTypeGetIsByRef (memory, type);
		}

		public override TargetAddress MonoTypeGetData (TargetMemoryAccess memory,
							       TargetAddress type)
		{
			return compat.MonoTypeGetData (memory, type);
		}

		public override TargetAddress MonoArrayTypeGetClass (TargetMemoryAccess memory,
								     TargetAddress atype)
		{
			return compat.MonoArrayTypeGetClass (memory, atype);
		}

		public override int MonoArrayTypeGetRank (TargetMemoryAccess memory,
							  TargetAddress atype)
		{
			return compat.MonoArrayTypeGetRank (memory, atype);
		}

		public override int MonoArrayTypeGetNumSizes (TargetMemoryAccess memory,
							      TargetAddress atype)
		{
			return compat.MonoArrayTypeGetNumSizes (memory, atype);
		}

		public override int MonoArrayTypeGetNumLoBounds (TargetMemoryAccess memory,
								 TargetAddress atype)
		{
			return compat.MonoArrayTypeGetNumLoBounds (memory, atype);
		}

		//
		// Fundamental types
		//

		public override TargetAddress GetBooleanClass (TargetMemoryAccess memory)
		{
			return compat.GetBooleanClass (memory);
		}

		public override TargetAddress GetCharClass (TargetMemoryAccess memory)
		{
			return compat.GetCharClass (memory);
		}

		public override TargetAddress GetSByteClass (TargetMemoryAccess memory)
		{
			return compat.GetSByteClass (memory);
		}

		public override TargetAddress GetByteClass (TargetMemoryAccess memory)
		{
			return compat.GetByteClass (memory);
		}

		public override TargetAddress GetInt16Class (TargetMemoryAccess memory)
		{
			return compat.GetInt16Class (memory);
		}

		public override TargetAddress GetUInt16Class (TargetMemoryAccess memory)
		{
			return compat.GetUInt16Class (memory);
		}

		public override TargetAddress GetInt32Class (TargetMemoryAccess memory)
		{
			return compat.GetInt32Class (memory);
		}

		public override TargetAddress GetUInt32Class (TargetMemoryAccess memory)
		{
			return compat.GetUInt32Class (memory);
		}

		public override TargetAddress GetInt64Class (TargetMemoryAccess memory)
		{
			return compat.GetInt64Class (memory);
		}

		public override TargetAddress GetUInt64Class (TargetMemoryAccess memory)
		{
			return compat.GetUInt64Class (memory);
		}

		public override TargetAddress GetSingleClass (TargetMemoryAccess memory)
		{
			return compat.GetSingleClass (memory);
		}

		public override TargetAddress GetDoubleClass (TargetMemoryAccess memory)
		{
			return compat.GetDoubleClass (memory);
		}

		public override TargetAddress GetIntPtrClass (TargetMemoryAccess memory)
		{
			return compat.GetIntPtrClass (memory);
		}

		public override TargetAddress GetUIntPtrClass (TargetMemoryAccess memory)
		{
			return compat.GetUIntPtrClass (memory);
		}

		public override TargetAddress GetVoidClass (TargetMemoryAccess memory)
		{
			return compat.GetVoidClass (memory);
		}

		public override TargetAddress GetStringClass (TargetMemoryAccess memory)
		{
			return compat.GetStringClass (memory);
		}

		public override TargetAddress GetObjectClass (TargetMemoryAccess memory)
		{
			return compat.GetObjectClass (memory);
		}

		public override TargetAddress GetArrayClass (TargetMemoryAccess memory)
		{
			return compat.GetArrayClass (memory);
		}

		public override TargetAddress GetDelegateClass (TargetMemoryAccess memory)
		{
			return compat.GetDelegateClass (memory);
		}

		public override TargetAddress GetExceptionClass (TargetMemoryAccess memory)
		{
			return compat.GetExceptionClass (memory);
		}

		//
		// `terrania' API
		//

		delegate bool MonoDbgMemoryAccess (IntPtr address, IntPtr buffer, int size);

		[DllImport("mono-dbg")]
		static extern bool mono_dbg_read_generic_class (MonoDbgMemoryAccess memory, IntPtr address,
								out MonoDbgGenericClass info);

		[StructLayout(LayoutKind.Sequential)]
		struct MonoDbgGenericClass {
			public IntPtr container_class;
			public IntPtr generic_inst;
			public IntPtr klass;
		}

		public override GenericClassInfo GetGenericClass (TargetMemoryAccess memory,
								  TargetAddress address)
		{
			MonoDbgGenericClass info;
			if (!mono_dbg_read_generic_class (
				    memory.ReadMemory, new IntPtr (address.Address), out info))
				return null;

			TargetAddress[] type_args = GetGenericInst (
				memory, new TargetAddress (memory.AddressDomain, info.generic_inst));
			if (type_args == null)
				return null;

			return new GenericClassInfo (
				new TargetAddress (memory.AddressDomain, info.container_class),
				type_args,
				new TargetAddress (memory.AddressDomain, info.klass));
		}
		[DllImport("mono-dbg")]
		extern static bool mono_dbg_read_generic_inst (MonoDbgMemoryAccess memory, IntPtr address,
							       out MonoDbgGenericInst info);

		[StructLayout(LayoutKind.Sequential)]
		struct MonoDbgGenericInst {
			public int id;
			public int type_argc;
			public IntPtr type_argv;
		}

		public TargetAddress[] GetGenericInst (TargetMemoryAccess memory, TargetAddress address)
		{
			MonoDbgGenericInst info;
			if (!mono_dbg_read_generic_inst (
				    memory.ReadMemory, new IntPtr (address.Address), out info))
				return null;

			TargetAddress[] type_argv = new TargetAddress [info.type_argc];
			if (memory.TargetMemoryInfo.TargetAddressSize == 4) {
				int[] temp = new int [info.type_argc];
				Marshal.Copy (info.type_argv, temp, 0, info.type_argc);
				for (int i = 0; i < info.type_argc; i++)
					type_argv [i] = new TargetAddress (memory.AddressDomain, temp [i]);
			} else {
				long[] temp = new long [info.type_argc];
				Marshal.Copy (info.type_argv, temp, 0, info.type_argc);
				for (int i = 0; i < info.type_argc; i++)
					type_argv [i] = new TargetAddress (memory.AddressDomain, temp [i]);
			}

			return type_argv;
		}
	}
}

#endif
