using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages
{
	public class BitfieldTargetLocation : TargetLocation
	{
		TargetLocation relative_to;
		int bit_offset, bit_size;

		public BitfieldTargetLocation (TargetLocation relative_to, int offset, int size)
			: base (relative_to.StackFrame, false, 0)
		{
			this.relative_to = relative_to;
			this.bit_offset = offset;
			this.bit_size = size;

			relative_to.LocationInvalidEvent += new LocationEventHandler (location_invalid);
		}

		void location_invalid (TargetLocation location)
		{
			SetInvalid ();
		}

		public override bool HasAddress {
			get { return false; }
		}

		protected override TargetAddress GetAddress ()
		{
			throw new InvalidOperationException ();
		}

		public override ITargetMemoryReader ReadMemory (int size)
		{
			byte[] data = relative_to.ReadBuffer (size);

			bool[] bit_data = new bool [8 * data.Length];
			bool[] target_bits = new bool [8 * data.Length];

			int bit_pos = 0;
			for (int i = 0; i < data.Length; i++) {
				byte current = data [i];
				for (int j = 0; j < 8; j++)
					bit_data [bit_pos++] = (current & (1 << j)) != 0;
			}

			bit_pos = 0;
			if (bit_offset + bit_size > target_bits.Length)
				bit_size = target_bits.Length - bit_offset;

			for (int i = bit_offset; i < bit_offset + bit_size; i++)
				target_bits [bit_pos++] = bit_data [i];

			byte[] target = new byte [data.Length];
			bit_pos = 0;
			for (int i = 0; i < target.Length; i++) {
				int current = 0;
				for (int j = 0; j < 8; j++) {
					if (target_bits [bit_pos++])
						current |= 1 << j;
				}
				target [i] = (byte) current;
			}

			return new TargetReader (target, TargetAccess);
		}

		protected override TargetLocation Clone (long offset)
		{
			throw new InvalidOperationException ();
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}:{2}", relative_to,
					      bit_offset, bit_size);
		}
	}
}
