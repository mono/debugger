using System;

namespace Mono.Debugger
{
	public class TargetBinaryReader
	{
		ITargetInfo target_info;
		byte[] contents;
		int pos;

		public TargetBinaryReader (byte[] contents, ITargetInfo target_info)
		{
			this.contents = contents;
			this.target_info = target_info;
		}

		public int AddressSize {
			get {
				if (target_info == null)
					throw new TargetMemoryException ("Can't get target address size");

				int address_size = target_info.TargetAddressSize;
				if ((address_size != 4) && (address_size != 8))
					throw new TargetMemoryException (
						"Unknown target address size " + address_size);

				return address_size;
			}
		}

		public ITargetInfo TargetInfo {
			get {
				return target_info;
			}

			set {
				target_info = value;
			}
		}

		public long Size {
			get {
				return contents.Length;
			}
		}

		public long Position {
			get {
				return pos;
			}

			set {
				pos = (int) value;
			}
		}

		public bool IsEof {
			get {
				return pos == contents.Length;
			}
		}

		public byte PeekByte (long pos)
		{
			return contents[pos];
		}

		public byte PeekByte ()
		{
			return contents[pos];
		}

		public byte ReadByte ()
		{
			return contents[pos++];
		}

		public short PeekInt16 (long pos)
		{
			return ((short) (contents[pos] | (contents[pos+1] << 8)));
		}

		public short ReadInt16 ()
		{
			short retval = PeekInt16 (pos);
			pos += 2;
			return retval;
		}

		public int PeekInt32 (long pos)
		{
			return (contents[pos] | (contents[pos+1] << 8) |
				(contents[pos+2] << 16) | (contents[pos+3] << 24));
		}

		public int ReadInt32 ()
		{
			int retval = PeekInt32 (pos);
			pos += 4;
			return retval;
		}

		public long PeekInt64 (long pos)
		{
			uint ret_low  = (uint) (contents[pos]           |
						(contents[pos+1] << 8)  |
						(contents[pos+2] << 16) |
						(contents[pos+3] << 24));
			uint ret_high = (uint) (contents[pos+4]         |
						(contents[pos+5] << 8)  |
						(contents[pos+6] << 16) |
						(contents[pos+7] << 24));
			return (long) ((((ulong) ret_high) << 32) | ret_low);
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
				return PeekInt32 (pos);
		}

		public long ReadAddress ()
		{
			if (AddressSize == 8)
				return ReadInt64 ();
			else
				return ReadInt32 ();
		}

		public string PeekString (long pos)
		{
			int length = 0;
			while (contents[pos+length] != 0)
				length++;

			char[] retval = new char [length];
			for (int i = 0; i < length; i++)
				retval [i] = (char) contents[pos+i];

			return new String (retval);
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

			Array.Copy (contents, pos, buffer, 0, size);

			return buffer;
		}

		public byte[] ReadBuffer (int size)
		{
			byte[] buffer = new byte [size];

			Array.Copy (contents, pos, buffer, 0, size);
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
	}
}
