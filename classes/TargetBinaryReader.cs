using System;

namespace Mono.Debugger
{
	// <summary>
	//   This is a generic binary reader.
	// </summary>
	[Serializable]
	public class TargetBinaryReader : TargetBinaryAccess
	{
		public TargetBinaryReader (byte[] contents, TargetInfo target_info)
			: base (new TargetBlob (contents, target_info))
		{ }

		public TargetBinaryReader (TargetBlob blob)
			: base (blob)
		{
		}

		public byte PeekByte (long pos)
		{
			return blob.Contents[pos];
		}

		public byte PeekByte ()
		{
			return blob.Contents[pos];
		}

		public byte ReadByte ()
		{
			return blob.Contents[pos++];
		}

		public short PeekInt16 (long pos)
		{
			if (swap)
				return ((short) (blob.Contents[pos+1] |
						 (blob.Contents[pos] << 8)));
			else
				return ((short) (blob.Contents[pos] |
						 (blob.Contents[pos+1] << 8)));
		}

		public short PeekInt16 ()
		{
			return PeekInt16 (pos);
		}

		public short ReadInt16 ()
		{
			short retval = PeekInt16 (pos);
			pos += 2;
			return retval;
		}

		public int PeekInt32 (long pos)
		{
			if (swap)
				return (blob.Contents[pos+3] |
					(blob.Contents[pos+2] << 8) |
					(blob.Contents[pos+1] << 16) |
					(blob.Contents[pos] << 24));
			else
				return (blob.Contents[pos] |
					(blob.Contents[pos+1] << 8) |
					(blob.Contents[pos+2] << 16) |
					(blob.Contents[pos+3] << 24));
		}

		public int PeekInt32 ()
		{
			return PeekInt32 (pos);
		}

		public int ReadInt32 ()
		{
			int retval = PeekInt32 (pos);
			pos += 4;
			return retval;
		}

		public uint PeekUInt32 (long pos)
		{
			if (swap)
				return ((uint) blob.Contents[pos+3] |
					((uint) blob.Contents[pos+2] << 8) |
					((uint) blob.Contents[pos+1] << 16) |
					((uint) blob.Contents[pos] << 24));
			else
				return ((uint) blob.Contents[pos] |
					((uint) blob.Contents[pos+1] << 8) |
					((uint) blob.Contents[pos+2] << 16) |
					((uint) blob.Contents[pos+3] << 24));
		}

		public uint PeekUInt32 ()
		{
			return PeekUInt32 (pos);
		}

		public uint ReadUInt32 ()
		{
			uint retval = PeekUInt32 (pos);
			pos += 4;
			return retval;
		}

		public long PeekInt64 (long pos)
		{
			uint ret_low, ret_high;
			if (swap) {
				ret_low  = (uint) (blob.Contents[pos+7]           |
						   (blob.Contents[pos+6] << 8)  |
						   (blob.Contents[pos+5] << 16) |
						   (blob.Contents[pos+4] << 24));
				ret_high = (uint) (blob.Contents[pos+3]         |
						   (blob.Contents[pos+2] << 8)  |
						   (blob.Contents[pos+1] << 16) |
						   (blob.Contents[pos] << 24));
			} else {
				ret_low  = (uint) (blob.Contents[pos]           |
						   (blob.Contents[pos+1] << 8)  |
						   (blob.Contents[pos+2] << 16) |
						   (blob.Contents[pos+3] << 24));
				ret_high = (uint) (blob.Contents[pos+4]         |
						   (blob.Contents[pos+5] << 8)  |
						   (blob.Contents[pos+6] << 16) |
						   (blob.Contents[pos+7] << 24));
			}
			return (long) ((((ulong) ret_high) << 32) | ret_low);
		}

		public long PeekInt64 ()
		{
			return PeekInt64 (pos);
		}

		public long ReadInt64 ()
		{
			long retval = PeekInt64 (pos);
			pos += 8;
			return retval;
		}

		public long PeekAddress (long pos)
		{
			if (AddressSize == 8)
				return PeekInt64 (pos);
			else
				return PeekUInt32 (pos);
		}

		public long PeekAddress ()
		{
			return PeekAddress (pos);
		}

		public long ReadAddress ()
		{
			if (AddressSize == 8)
				return ReadInt64 ();
			else
				return ReadUInt32 ();
		}

		public string PeekString (long pos)
		{
			int length = 0;
			while (blob.Contents[pos+length] != 0)
				length++;

			char[] retval = new char [length];
			for (int i = 0; i < length; i++)
				retval [i] = (char) blob.Contents[pos+i];

			return new String (retval);
		}

		public string PeekString ()
		{
			return PeekString (pos);
		}

		public string ReadString ()
		{
			string retval = PeekString (pos);
			pos += retval.Length + 1;
			return retval;
		}

		public byte[] PeekBuffer (long offset, int size)
		{
			byte[] buffer = new byte [size];

			Array.Copy (blob.Contents, (int) offset, buffer, 0, size);

			return buffer;
		}

		public byte[] PeekBuffer (int size)
		{
			return PeekBuffer (pos, size);
		}

		public byte[] ReadBuffer (int size)
		{
			byte[] buffer = new byte [size];

			Array.Copy (blob.Contents, pos, buffer, 0, size);
			pos += size;

			return buffer;
		}

		public int PeekLeb128 (long pos)
		{
			int size;
			return PeekLeb128 (pos, out size);
		}

		public int PeekLeb128 (long pos, out int size)
		{
			int ret = 0;
			int shift = 0;
			byte b;

			size = 0;
			do {
				b = PeekByte (pos + size);
				size++;
				
				ret = ret | ((b & 0x7f) << shift);
				shift += 7;
			} while ((b & 0x80) == 0x80);

			return ret;
		}

		public int ReadLeb128 ()
		{
			int size;
			int retval = PeekLeb128 (pos, out size);
			pos += size;
			return retval;
		}

		public int PeekSLeb128 (long pos)
		{
			int size;
			return PeekSLeb128 (pos, out size);
		}

		public int PeekSLeb128 (long pos, out int size)
		{
			int ret = 0;
			int shift = 0;
			byte b;

			size = 0;
			do {
				b = (byte) PeekByte (pos + size);
				size++;
				
				ret = ret | ((b & 0x7f) << shift);
				shift += 7;
			} while ((b & 0x80) == 0x80);

			if ((shift < 31) && ((b & 0x40) == 0x40))
				ret |= - (1 << shift);

			return ret;
		}

		public int ReadSLeb128 ()
		{
			int size;
			int retval = PeekSLeb128 (pos, out size);
			pos += size;
			return retval;
		}

		public long ReadInteger (int size)
		{
			switch (size) {
			case 1:
				return ReadByte ();

			case 2:
				return ReadInt16 ();

			case 4:
				return ReadInt32 ();

			case 8:
				return ReadInt64 ();

			default:
				throw new TargetMemoryException (
					"Unknown integer size " + size);
			}
		}

	}
}
