using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   The location of a variable.
	// </summary>
	[Serializable]
	internal class MonoVariableLocation : TargetLocation
	{
		bool is_regoffset;
		int register;
		long regoffset;

		public MonoVariableLocation (StackFrame frame, bool is_regoffset, int register,
					     long regoffset, bool is_byref)
			: this (frame, is_regoffset, register, regoffset, is_byref, 0)
		{ }

		protected MonoVariableLocation (StackFrame frame, bool is_regoffset, int register,
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
			Register reg = frame.Registers [register];
			if (!reg.Valid)
				throw new LocationInvalidException ();

			long contents = reg.Value;

			TargetAddress address = new TargetAddress (
				TargetMemoryInfo.AddressDomain, contents + regoffset);

			if (is_byref && is_regoffset)
				address = TargetMemoryAccess.ReadAddress (address);

			return address;
		}

		public override ITargetMemoryReader ReadMemory (int size)
		{
			if (HasAddress)
				return base.ReadMemory (size);

			// If this is a valuetype, the register hold the whole data.
			long contents = frame.GetRegister (register);
			contents += regoffset;

			// We can read at most Inferior.TargetIntegerSize from a register
			// (a word on the target).
			if ((offset < 0) || (offset + size > TargetMemoryInfo.TargetIntegerSize))
				throw new ArgumentException ();

			// Using ITargetMemoryReader for this is just an ugly hack, but I
			// wanted to hide the fact that the data is cominig from a
			// register from the caller.
			ITargetMemoryReader reader;
			byte[] buffer;
			if (TargetMemoryInfo.TargetIntegerSize == 4)
				buffer = BitConverter.GetBytes ((int) contents);
			else if (TargetMemoryInfo.TargetIntegerSize == 8)
				buffer = BitConverter.GetBytes (contents);
			else
				throw new InternalError ();

			reader = new TargetReader (buffer, TargetMemoryInfo);
			reader.Offset = offset;
			return reader;
		}

		public override void WriteBuffer (byte[] data)
		{
			if (HasAddress) {
				base.WriteBuffer (data);
				return;
			}

			long contents;

			if (data.Length > TargetMemoryInfo.TargetIntegerSize)
				throw new InternalError ();

			if (data.Length < TargetMemoryInfo.TargetIntegerSize) {
				switch (data.Length) {
				case 1: contents = data[0]; break;
				case 2: contents = BitConverter.ToInt16 (data, 0); break;
				case 4: contents = BitConverter.ToInt32 (data, 0); break;
				default:
				  throw new InternalError ();
				}
			}
			else if (TargetMemoryInfo.TargetIntegerSize == 4)
				contents = BitConverter.ToInt32 (data, 0);
			else if (TargetMemoryInfo.TargetIntegerSize == 8)
				contents = BitConverter.ToInt64 (data, 0);
			else
				throw new InternalError ();

			// If this is a valuetype, the register hold the whole data.
			frame.SetRegister (register, contents);
		}

		public override void WriteAddress (TargetAddress address)
		{
			if (is_regoffset) {
				// If this is a reference type, the register just holds the
				// address of the actual data, so read the address from the
				// register and return it.
				long contents = frame.GetRegister (register);
				if (contents == 0)
					throw new LocationInvalidException ();

				TargetAddress taddress = new TargetAddress (
					TargetMemoryInfo.AddressDomain, contents + regoffset);
				TargetMemoryAccess.WriteAddress (taddress, address);
			} else {
				frame.SetRegister (register, address.Address);
			}
		}

		protected override TargetLocation Clone (long new_offset)
		{
			return new MonoVariableLocation (
				frame, is_regoffset, register, regoffset, IsByRef,
				offset + new_offset);
		}

		public override string Print ()
		{
			int regindex = frame.Registers [register].Index;
			string name = frame.Process.Architecture.RegisterNames [regindex];

			long the_offset = regoffset + offset;
			if (the_offset > 0)
				return String.Format ("%{0}+0x{1:x}", name, the_offset);
			else if (offset < 0)
				return String.Format ("%{0}-0x{1:x}", name, -the_offset);
			else
				return String.Format ("%{0}", name);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}:{2:x}", is_regoffset, register, regoffset);
		}
	}
}
