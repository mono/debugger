using System;

namespace Mono.Debugger.Backends
{
	// <summary>
	//   The location of a variable.
	// </summary>
	internal class MonoVariableLocation : TargetLocation
	{
		bool is_regoffset;
		int register;
		long regoffset;

		public MonoVariableLocation (StackFrame frame, bool is_regoffset, int register,
					     long regoffset, bool is_byref, long offset)
			: base (frame, is_byref, offset)
		{
			this.is_regoffset = is_regoffset;
			this.register = register;
			this.regoffset = regoffset;
		}

		public override bool HasAddress {
			get { return is_regoffset || IsByRef; }
		}

		protected override TargetAddress GetAddress ()
		{
			if (!HasAddress)
				throw new InvalidOperationException ();

			// If this is a reference type, the register just holds the
			// address of the actual data, so read the address from the
			// register and return it.
			long contents = frame.GetRegister (register);
			if (contents == 0)
				throw new LocationInvalidException ();

			TargetAddress address = new TargetAddress (TargetMemoryAccess.AddressDomain, contents + regoffset);
			if (is_byref)
				address = TargetMemoryAccess.ReadAddress (address);

			return address;
		}

		public override ITargetMemoryReader ReadMemory (int size)
		{
			if (HasAddress)
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

		protected override TargetLocation Clone (long offset)
		{
			return new MonoVariableLocation (
				frame, is_regoffset, register, regoffset, IsByRef, Offset + offset);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}:{2:x}", is_regoffset, register, regoffset);
		}
	}
}
