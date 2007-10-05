using System;
using System.Text;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages
{
	internal class BitfieldTargetLocation : TargetLocation
	{
		TargetLocation relative_to;
		int bit_offset, bit_size;

		public BitfieldTargetLocation (TargetLocation relative_to, int offset, int size)
		{
			this.relative_to = relative_to;
			this.bit_offset = offset;
			this.bit_size = size;
		}

		internal override bool HasAddress {
			get { return false; }
		}

		internal override TargetAddress GetAddress (TargetMemoryAccess target)
		{
			throw new InvalidOperationException ();
		}

		private string Print (bool[] data)
		{
			StringBuilder sb = new StringBuilder ("[");
			for (int i = 0; i < data.Length; i++) {
				if ((i > 0) && ((i % 8) == 0))
					sb.Append (" ");
				sb.Append (data [i] ? "1" : "0");
			}
			return sb.ToString ();
		}

		internal override TargetBlob ReadMemory (TargetMemoryAccess target, int size)
		{
			byte[] data = relative_to.ReadBuffer (target, size);

			int total_size = 8 * data.Length;
			bool[] bit_data = new bool [total_size];
			bool[] target_bits = new bool [total_size];

			// FIXME
			bool is_bigendian = false;

			int bit_pos = 0;
			for (int i = 0; i < data.Length; i++) {
				byte current = data [i];
				for (int j = 0; j < 8; j++)
					bit_data [bit_pos++] = (current & (1 << j)) != 0;
			}

			bit_pos = 0;
			if (!is_bigendian)
				bit_offset = total_size - bit_offset - bit_size;

			for (int i = bit_offset; i < bit_offset + bit_size; i++)
				target_bits [bit_pos++] = bit_data [i];

			byte[] blob = new byte [data.Length];
			bit_pos = 0;
			for (int i = 0; i < blob.Length; i++) {
				int current = 0;
				for (int j = 0; j < 8; j++) {
					if (target_bits [bit_pos++])
						current |= 1 << j;
				}
				blob [i] = (byte) current;
			}

			return new TargetBlob (blob, target.TargetInfo);
		}

		public override string Print ()
		{
			return String.Format ("Bitfield [{0}..{1}] in {2}",
					      bit_offset, bit_offset + bit_size,
					      relative_to.Print ());
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}:{2}", relative_to,
					      bit_offset, bit_size);
		}
	}
}
