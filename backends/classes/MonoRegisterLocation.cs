using System;

namespace Mono.Debugger.Backends
{
	internal class MonoRegisterLocation : MonoTargetLocation
	{
		int register;

		internal MonoRegisterLocation (StackFrame frame, bool is_byref, int register, long offset)
			: base (frame, is_byref, offset)
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

			return new TargetAddress (TargetMemoryAccess.AddressDomain, contents);
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
			if ((Offset < 0) || (Offset + size > memory.TargetIntegerSize))
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
			return new MonoRegisterLocation (
				frame, is_byref, register, Offset + offset);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0:x}", register);
		}
	}
}
