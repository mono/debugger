using System;
using System.Text;
using Cecil = Mono.Cecil;

using Mono.Debugger;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	public class MonoGenericContext : DebuggerMarshalByRefObject
	{
		public readonly MonoGenericInst MethodInst;
		public readonly MonoGenericInst ClassInst;

		internal static MonoGenericContext ReadGenericContext (MonoLanguageBackend mono,
								       TargetMemoryAccess memory,
								       TargetAddress address)
		{
			MonoGenericInst class_inst = null;
			TargetAddress class_inst_addr = memory.ReadAddress (address);
			if (!class_inst_addr.IsNull)
				class_inst = MonoGenericInst.ReadGenericInst (
					mono, memory, class_inst_addr);

			MonoGenericInst method_inst = null;
			TargetAddress method_inst_addr = memory.ReadAddress (
				address + memory.TargetMemoryInfo.TargetAddressSize);
			if (!method_inst_addr.IsNull)
				method_inst = MonoGenericInst.ReadGenericInst (
					mono, memory, method_inst_addr);

			return new MonoGenericContext (method_inst, class_inst);
		}

		protected MonoGenericContext (MonoGenericInst method_inst, MonoGenericInst class_inst)
		{
			this.MethodInst = method_inst;
			this.ClassInst = class_inst;
		}

		public override string ToString ()
		{
			return String.Format ("MonoGenericContext ({0}:{1})",
					      MethodInst, ClassInst);
		}
	}

	public class MonoGenericInst : DebuggerMarshalByRefObject
	{
		public readonly TargetType[] Types;

		public MonoGenericInst (TargetType[] types)
		{
			this.Types = types;
		}

		internal static MonoGenericInst ReadGenericInst (MonoLanguageBackend mono,
								 TargetMemoryAccess memory,
								 TargetAddress address)
		{
			int addr_size = memory.TargetMemoryInfo.TargetAddressSize;

			TargetReader blob = new TargetReader (memory.ReadMemory (address, 16));
			int header = memory.ReadInteger (address + 4);
			TargetAddress type_argv_ptr = memory.ReadAddress (address + 8);

			int type_argc = header & 0x3fffff;

			TargetType[] types = new TargetType [type_argc];
			for (int i = 0; i < type_argc; i++) {
				TargetAddress ptr = memory.ReadAddress (type_argv_ptr + i * addr_size);
				types [i] = mono.ReadType (memory, ptr);
			}

			return new MonoGenericInst (types);
		}

		public override string ToString ()
		{
			StringBuilder sb = new StringBuilder ("MonoGenericInst (");
			for (int i = 0; i < Types.Length; i++) {
				if (i > 0)
					sb.Append (",");
				sb.Append (Types [i]);
			}
			sb.Append (")");
			return sb.ToString ();
		}
	}
}
