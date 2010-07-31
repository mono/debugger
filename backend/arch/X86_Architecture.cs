using System;
using System.Collections;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	// Keep in sync with DebuggerRegisters in backends/server/x86-arch.h.
	internal enum X86_Register
	{
		RAX	= 0,
		RCX,
		RDX,
		RBX,

		RSP,
		RBP,
		RSI,
		RDI,

		R8,
		R9,
		R10,
		R11,
		R12,
		R13,
		R14,
		R15,

		RIP,
		EFLAGS,

		ORIG_RAX,
		CS,
		SS,
		DS,
		ES,
		FS,
		GS,

		FS_BASE,
		GS_BASE,

		COUNT
	}

	internal abstract class X86_Architecture : Architecture
	{
		protected X86_Architecture (Process process, TargetInfo info)
			: base (process, info)
		{ }

		internal override int CountRegisters {
			get {
				return (int) X86_Register.COUNT;
			}
		}
	}
}
