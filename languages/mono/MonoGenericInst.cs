using System;
using System.Text;
using Cecil = Mono.Cecil;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInst : DebuggerMarshalByRefObject
	{
		public readonly TargetType[] Types;

		public MonoGenericInst (MonoLanguageBackend mono, TargetMemoryAccess memory,
					TargetAddress address)
		{
			Console.WriteLine ("NEW GENERIC INST: {0}", address);

			int addr_size = memory.TargetInfo.TargetAddressSize;

			int header = memory.ReadInteger (address);
			TargetAddress type_argv_ptr = memory.ReadAddress (address + addr_size);

			int type_argc = (header & 0x7ff8) >> 3;

			Console.WriteLine ("NEW GENERIC INST #2: {0:x} {1} {2}",
					   header, type_argc, type_argv_ptr);

			Types = new TargetType [type_argc];
			for (int i = 0; i < type_argc; i++) {
				TargetAddress ptr = memory.ReadAddress (type_argv_ptr + i * addr_size);
				Types [i] = MonoType.Read (mono, memory, ptr);
				Console.WriteLine ("NEW GENERIC INST #3: {0} {1}", i, Types [i]);
			}
		}
	}
}
