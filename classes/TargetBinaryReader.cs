using System;
using System.Text;

namespace Mono.Debugger
{
	public sealed class TargetBlob
	{
		public readonly byte[] Contents;

		public TargetBlob (byte[] contents)
		{
			this.Contents = contents;
		}
	}

	// <summary>
	//   This is a generic binary reader.
	// </summary>
	public class TargetBinaryReader
	{
		ITargetInfo target_info;
		TargetBlob blob;
		int pos;

		public TargetBinaryReader (byte[] contents, ITargetInfo target_info)
			: this (new TargetBlob (contents), target_info)
		{ }

		public TargetBinaryReader (TargetBlob blob, ITargetInfo target_info)
		{
			this.blob = blob;
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
				return blob.Contents.Length;
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
				return pos == blob.Contents.Length;
			}
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
			return ((short) (blob.Contents[pos] | (blob.Contents[pos+1] << 8)));
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
			return (blob.Contents[pos] | (blob.Contents[pos+1] << 8) |
				(blob.Contents[pos+2] << 16) | (blob.Contents[pos+3] << 24));
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

		public long PeekInt64 (long pos)
		{
			uint ret_low  = (uint) (blob.Contents[pos]           |
						(blob.Contents[pos+1] << 8)  |
						(blob.Contents[pos+2] << 16) |
						(blob.Contents[pos+3] << 24));
			uint ret_high = (uint) (blob.Contents[pos+4]         |
						(blob.Contents[pos+5] << 8)  |
						(blob.Contents[pos+6] << 16) |
						(blob.Contents[pos+7] << 24));
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
				return PeekInt32 (pos);
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
				return ReadInt32 ();
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

		public string HexDump ()
		{
			return HexDump (blob.Contents);
		}

		public static string HexDump (byte[] data)
		{
			StringBuilder sb = new StringBuilder ();

			for (int i = 0; i < data.Length; i++) {
				if (i > 0)
					sb.Append (" ");
				sb.Append (String.Format ("{1}{0:x}", data [i], data [i] >= 16 ? "" : "0"));
			}
			return sb.ToString ();
		}

		public static string HexDump (TargetAddress start, byte[] data)
		{
			StringBuilder sb = new StringBuilder ();

			sb.Append (String.Format ("{0}   ", start));

			for (int i = 0; i < data.Length; i++) {
				if (i > 0) {
					if ((i % 16) == 0) {
						start += 16;
						sb.Append (String.Format ("\n{0}   ", start));
					} else if ((i % 8) == 0)
						sb.Append (" - ");
					else
						sb.Append (" ");
				}
				sb.Append (String.Format ("{1}{0:x}", data [i], data [i] >= 16 ? "" : "0"));
			}
			return sb.ToString ();
		}
	}
}
