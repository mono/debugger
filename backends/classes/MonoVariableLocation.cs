using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   The location of a variable.
	// </summary>
	[Serializable]
	internal sealed class MonoVariableLocation : TargetLocation
	{
		bool is_regoffset;
		int register;
		long regoffset;

		TargetAddress address;

		public MonoVariableLocation (StackFrame frame, bool is_regoffset, int register,
					     long regoffset, bool is_byref)
			: base (frame, is_byref)
		{
			this.is_regoffset = is_regoffset;
			this.register = register;
			this.regoffset = regoffset;

			update ();
		}

		void update ()
		{
			// If this is a reference type, the register just holds the
			// address of the actual data, so read the address from the
			// register and return it.
			Register reg = frame.Registers [register];
			if (!reg.Valid)
				throw new LocationInvalidException ();

			long contents = reg.Value;

			address = new TargetAddress (
				TargetMemoryInfo.AddressDomain, contents + regoffset);

			if (is_byref && is_regoffset)
				address = TargetMemoryAccess.ReadAddress (address);
		}

		public override bool HasAddress {
			get { return is_regoffset || IsByRef; }
		}

		protected override TargetAddress GetAddress ()
		{
			return address;
		}

		public override TargetBlob ReadMemory (int size)
		{
			if (HasAddress)
				return base.ReadMemory (size);

			// If this is a valuetype, the register hold the whole data.
			long contents = address.Address;

			byte[] buffer;
			if (size == 4)
				buffer = BitConverter.GetBytes ((int) contents);
			else if (size == 8)
				buffer = BitConverter.GetBytes (contents);
			else
				throw new ArgumentException ();

			return new TargetBlob (buffer, TargetInfo);
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
			update ();
		}

		public override void WriteAddress (TargetAddress new_address)
		{
			if (is_regoffset) {
				TargetMemoryAccess.WriteAddress (address, new_address);
			} else {
				frame.SetRegister (register, new_address.Address);
				update ();
			}
		}

		public override string Print ()
		{
			int regindex = frame.Registers [register].Index;
			string name = frame.Process.Architecture.RegisterNames [regindex];

			if (regoffset > 0)
				return String.Format ("%{0}+0x{1:x}", name, regoffset);
			else if (regoffset < 0)
				return String.Format ("%{0}-0x{1:x}", name, -regoffset);
			else
				return String.Format ("%{0}", name);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}:{2:x}", is_regoffset, register, regoffset);
		}
	}
}
