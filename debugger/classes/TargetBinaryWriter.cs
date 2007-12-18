using System;
using System.Text;

namespace Mono.Debugger
{
	public class TargetBinaryWriter : TargetBinaryAccess
	{
		public TargetBinaryWriter (int size, TargetMemoryInfo target_info)
			: base (new TargetBlob (size, target_info))
		{ }

		public void PokeByte (long pos, byte value)
		{
			blob.Contents[pos] = value;
		}

		public void PokeByte (byte value)
		{
			blob.Contents[pos] = value;
		}

		public void WriteByte (byte value)
		{
			blob.Contents[pos++] = value;
		}

		public void PokeInt16 (long pos, short value)
		{
			blob.Contents[pos] = (byte) (value & 0x00ff);
			blob.Contents[pos+1] = (byte) (value >> 8);
		}

		public void PokeInt16 (short value)
		{
			PokeInt16 (pos, value);
		}

		public void WriteInt16 (short value)
		{
			PokeInt16 (pos, value);
			pos += 2;
		}

		public void PokeInt32 (long pos, int value)
		{
			blob.Contents[pos] = (byte) (value & 0x000000ff);
			blob.Contents[pos+1] = (byte) ((value & 0x0000ff00) >> 8);
			blob.Contents[pos+2] = (byte) ((value & 0x00ff0000) >> 16);
			blob.Contents[pos+3] = (byte) ((value & 0xff000000) >> 24);
		}

		public void PokeInt32 (int value)
		{
			PokeInt32 (pos, value);
		}

		public void WriteInt32 (int value)
		{
			PokeInt32 (pos, value);
			pos += 4;
		}

		public void PokeInt64 (long pos, long value)
		{
			unchecked {
				ulong uvalue = (ulong) value;
				ulong low = uvalue & 0x00000000ffffffffL;
				ulong high = (uvalue & 0xffffffff00000000L) >> 32;
				PokeInt32 (pos, (int) low);
				PokeInt32 (pos+4, (int) high);
			}
		}

		public void PokeInt64 (long value)
		{
			PokeInt64 (pos, value);
		}

		public void WriteInt64 (long value)
		{
			PokeInt64 (pos, value);
			pos += 8;
		}

		public void PokeAddress (long pos, long value)
		{
			if (AddressSize == 8)
				PokeInt64 (pos, value);
			else
				PokeInt32 (pos, (int) value);
		}

		public void PokeAddress (long value)
		{
			PokeAddress (pos, value);
		}

		public void PokeAddress (TargetAddress address)
		{
			PokeAddress (pos, address.Address);
		}

		public void WriteAddress (long value)
		{
			if (AddressSize == 8)
				WriteInt64 (value);
			else
				WriteInt32 ((int) value);
		}

		public void WriteAddress (TargetAddress address)
		{
			if (AddressSize == 8)
				WriteInt64 (address.Address);
			else
				WriteInt32 ((int) address.Address);
		}

		public void PokeBuffer (long pos, byte[] buffer)
		{
			Array.Copy (buffer, 0, blob.Contents, (int) pos, buffer.Length);
		}

		public void WriteBuffer (byte[] buffer)
		{
			Array.Copy (buffer, 0, blob.Contents, pos, buffer.Length);
			pos += buffer.Length;
		}
	}
}
