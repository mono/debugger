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
		bool is_byref;
		bool is_regoffset;
		Register register;
		long regoffset;

		TargetAddress address;

		public MonoVariableLocation (TargetAccess target, bool is_regoffset,
					     Register register, long regoffset, bool is_byref)
		{
			this.is_regoffset = is_regoffset;
			this.register = register;
			this.regoffset = regoffset;
			this.is_byref = is_byref;

			update (target);
		}

		void update (TargetAccess target)
		{
			// If this is a reference type, the register just holds the
			// address of the actual data, so read the address from the
			// register and return it.
			if (!register.Valid)
				throw new LocationInvalidException ();

			long contents = register.Value;

			if (contents == 0)
				address = TargetAddress.Null;
			else
				address = new TargetAddress (
					target.TargetMemoryInfo.AddressDomain, contents + regoffset);

			if (is_byref && is_regoffset)
				address = target.TargetMemoryAccess.ReadAddress (address);
		}

		public override bool HasAddress {
			get { return is_regoffset || is_byref; }
		}

		public override TargetAddress Address {
			get { return address; }
		}

		internal override TargetBlob ReadMemory (TargetAccess target, int size)
		{
			if (HasAddress)
				return base.ReadMemory (target, size);

			// If this is a valuetype, the register hold the whole data.
			long contents = address.Address;

			byte[] buffer;
			if (size == 1)
				buffer = BitConverter.GetBytes ((byte) contents);
			else if (size == 2)
				buffer = BitConverter.GetBytes ((short) contents);
			else if (size == 4)
				buffer = BitConverter.GetBytes ((int) contents);
			else if (size == 8)
				buffer = BitConverter.GetBytes (contents);
			else
				throw new ArgumentException ();

			return new TargetBlob (buffer, target.TargetInfo);
		}

		internal override void WriteBuffer (TargetAccess target, byte[] data)
		{
			if (HasAddress) {
				base.WriteBuffer (target, data);
				return;
			}

			long contents;

			if (data.Length > target.TargetMemoryInfo.TargetIntegerSize)
				throw new InternalError ();

			if (data.Length < target.TargetMemoryInfo.TargetIntegerSize) {
				switch (data.Length) {
				case 1: contents = data[0]; break;
				case 2: contents = BitConverter.ToInt16 (data, 0); break;
				case 4: contents = BitConverter.ToInt32 (data, 0); break;
				default:
				  throw new InternalError ();
				}
			}
			else if (target.TargetMemoryInfo.TargetIntegerSize == 4)
				contents = BitConverter.ToInt32 (data, 0);
			else if (target.TargetMemoryInfo.TargetIntegerSize == 8)
				contents = BitConverter.ToInt64 (data, 0);
			else
				throw new InternalError ();

			// If this is a valuetype, the register hold the whole data.
			register.WriteRegister (target, contents);
			update (target);
		}

		internal override void WriteAddress (TargetAccess target, TargetAddress new_address)
		{
			if (is_regoffset) {
				TargetAddress the_addr;
				if (is_byref)
					the_addr = new TargetAddress (
						target.TargetMemoryInfo.AddressDomain,
						register.Value + regoffset);
				else
					the_addr = address;

				target.TargetMemoryAccess.WriteAddress (the_addr, new_address);
				update (target);
			} else {
				register.WriteRegister (target, new_address.Address);
				update (target);
			}
		}

		public override string Print ()
		{
			int regindex = register.Index;
			if (regoffset > 0)
				return String.Format ("%{0}+0x{1:x}", register.Index, regoffset);
			else if (regoffset < 0)
				return String.Format ("%{0}-0x{1:x}", register.Index, -regoffset);
			else
				return String.Format ("%{0}", register.Index);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}:{2:x}", is_regoffset, register, regoffset);
		}
	}
}
