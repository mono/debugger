using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoRegisterLocation : MonoTargetLocation
	{
		int register;

		internal MonoRegisterLocation (DebuggerBackend backend, StackFrame frame,
					       bool is_byref, int register, long offset,
					       TargetAddress start_scope, TargetAddress end_scope)
			: base (backend, frame, is_byref, offset, start_scope, end_scope)
		{
			this.register = register;
		}

		public override bool HasAddress {
			get {
				return IsByRef;
			}
		}

		protected override TargetAddress GetAddress ()
		{
			// If this is a reference type, the register just holds the
			// address of the actual data, so read the address from the
			// register and return it.
			long contents = frame.GetRegister (register);
			if (contents == 0)
				throw new LocationInvalidException ();

			return new TargetAddress (TargetMemoryAccess, contents);
		}

		public override ITargetMemoryReader ReadMemory (int size)
		{
			if (IsByRef)
				return base.ReadMemory (size);

			// If this is a valuetype, the register hold the whole data.
			long contents = frame.GetRegister (register);

			ITargetMemoryAccess memory = TargetMemoryAccess;

			// We can read at most Inferior.TargetIntegerSize from a register
			// (a word on the target).
			if (Offset + size > memory.TargetIntegerSize)
				throw new ArgumentException ();

			// Using ITargetMemoryReader for this is just an ugly hack, but I
			// wanted to hide the fact that the data is cominig from a
			// register from the caller.
			ITargetMemoryReader reader;
			if (memory.TargetIntegerSize == 4)
				reader = memory.ReadMemory (BitConverter.GetBytes ((int) contents));
			else if (memory.TargetIntegerSize == 8)
				reader = memory.ReadMemory (BitConverter.GetBytes (contents));
			else
				throw new InternalError ();

			reader.Offset = Offset;
			return reader;
		}

		protected override MonoTargetLocation Clone (int offset)
		{
			return new MonoRegisterLocation (backend, frame, is_byref, register,
							 Offset + offset, start_scope, end_scope);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0:x}", register);
		}
	}
}
