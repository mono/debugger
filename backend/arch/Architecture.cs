using System;
using Mono.Debugger.Backends;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backends
{
	// <summary>
	//   Architecture-dependent interface.
	// </summary>
	internal abstract class Architecture : DebuggerMarshalByRefObject, IDisposable
	{
		protected readonly ProcessServant process;
		protected readonly TargetInfo TargetInfo;

		BfdDisassembler disassembler;
		Opcodes_X86 opcodes;

		protected Architecture (ProcessServant process, TargetInfo info)
		{
			this.process = process;
			this.TargetInfo = info;

			disassembler = new BfdDisassembler (process, info.TargetAddressSize == 8);
			opcodes = new Opcodes_X86 (process, info.TargetAddressSize == 8);
		}

		internal Disassembler Disassembler {
			get { return disassembler; }
		}

		public int TargetAddressSize {
			get { return TargetInfo.TargetAddressSize; }
		}

		// <summary>
		//   The names of all registers.
		// </summary>
		public abstract string[] RegisterNames {
			get;
		}

		// <summary>
		//   Indices of the "important" registers, sorted in a way that's suitable
		//   to display them to the user.
		// </summary>
		public abstract int[] RegisterIndices {
			get;
		}

		// <summary>
		//   Indices of all registers.
		// </summary>
		public abstract int[] AllRegisterIndices {
			get;
		}

		// <summary>
		// Size (in bytes) of each register.
		// </summary>
		public abstract int[] RegisterSizes {
			get;
		}

		// <summary>
		// A map between the register the register numbers in
		// the jit code generator and the register indices
		// used in the above arrays.
		// </summary>
		internal abstract int[] RegisterMap {
			get;
		}

		internal abstract int[] DwarfFrameRegisterMap {
			get;
		}

		internal abstract int CountRegisters {
			get;
		}

		public abstract string PrintRegister (Register register);

		public abstract string PrintRegisters (StackFrame frame);

		// <summary>
		//   Returns whether the instruction at target address @address is a `ret'
		//   instruction.
		// </summary>
		internal abstract bool IsRetInstruction (TargetMemoryAccess memory,
							 TargetAddress address);

		// <summary>
		//   Returns whether the instruction at target address @address is a `syscall'
		//   instruction.
		// </summary>
		internal abstract bool IsSyscallInstruction (TargetMemoryAccess memory,
							     TargetAddress address);

		internal Instruction ReadInstruction (TargetMemoryAccess memory, TargetAddress address)
		{
			return opcodes.ReadInstruction (memory, address);
		}

		internal abstract int MaxPrologueSize {
			get;
		}

		internal abstract Registers CopyRegisters (Registers regs);

		internal abstract StackFrame GetLMF (Thread thread);

		internal abstract StackFrame UnwindStack (StackFrame last_frame,
							  TargetMemoryAccess memory,
							  byte[] code, int offset);

		internal abstract StackFrame TrySpecialUnwind (StackFrame last_frame,
							       TargetMemoryAccess memory);

		internal abstract StackFrame CreateFrame (Thread thread, Registers regs,
							  bool adjust_retaddr);

		protected abstract TargetAddress AdjustReturnAddress (Thread thread,
								      TargetAddress address);

		internal StackFrame GetCallbackFrame (ThreadServant servant, StackFrame frame,
						      bool exact_match)
		{
			Registers callback = servant.GetCallbackFrame (frame.StackPointer, exact_match);
			if (callback != null)
				return CreateFrame (frame.Thread, callback, false);

			return null;
		}

		internal StackFrame CreateFrame (Thread thread, TargetAddress address,
						 TargetAddress stack, TargetAddress frame_pointer,
						 Registers regs, bool adjust_retaddr)
		{
			if ((address.IsNull) || (address.Address == 0))
				return null;

			if (adjust_retaddr) {
				TargetAddress old_address = address;
				try {
					address = AdjustReturnAddress (thread, old_address);
				} catch {
					address = old_address;
				}
			}

			Method method = process.SymbolTableManager.Lookup (address);
			if (method != null)
				return new StackFrame (
					thread, address, stack, frame_pointer, regs, method);

			Symbol name = process.SymbolTableManager.SimpleLookup (address, false);
			return new StackFrame (
				thread, address, stack, frame_pointer, regs,
				thread.NativeLanguage, name);
		}

		internal abstract void InterpretCallInstruction (Inferior inferior,
								 TargetAddress ret_addr,
								 TargetAddress call_target);

		//
		// This is a horrible hack - don't use !
		//
		//
		internal abstract void Hack_ReturnNull (Inferior inferior);

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Architecture");
		}

		protected virtual void DoDispose ()
		{
			if (disassembler != null) {
				disassembler.Dispose ();
				disassembler = null;
			}
		}

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing)
				DoDispose ();
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Architecture ()
		{
			Dispose (false);
		}

	}
}
