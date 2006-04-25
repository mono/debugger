using System;
using System.IO;
using System.Text;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Backends
{
	[Serializable]
	internal class TargetReader
	{
		byte[] data;
		TargetBinaryReader reader;
		TargetInfo info;

		internal TargetReader (byte[] data, TargetInfo info)
		{
			if ((info == null) || (data == null))
				throw new ArgumentNullException ();
			this.reader = new TargetBinaryReader (data, info);
			this.info = info;
			this.data = data;
		}

		internal TargetReader (TargetBlob data)
			: this (data.Contents, data.TargetInfo)
		{ }

		public long Offset {
			get {
				return reader.Position;
			}

			set {
				reader.Position = value;
			}
		}

		public long Size {
			get {
				return data.Length;
			}
		}

		public byte[] Contents {
			get {
				return data;
			}
		}

		public TargetBinaryReader BinaryReader {
			get {
				return reader;
			}
		}

		public int TargetIntegerSize {
			get {
				return info.TargetIntegerSize;
			}
		}

		public int TargetLongIntegerSize {
			get {
				return info.TargetLongIntegerSize;
			}
		}

		public int TargetAddressSize {
			get {
				return info.TargetAddressSize;
			}
		}

		public bool IsBigEndian {
			get {
				return info.IsBigEndian;
			}
		}

		public byte ReadByte ()
		{
			return reader.ReadByte ();
		}

		public int ReadInteger ()
		{
			return reader.ReadInt32 ();
		}

		public long ReadLongInteger ()
		{
			if (TargetLongIntegerSize == 4)
				return reader.ReadInt32 ();
			else if (TargetLongIntegerSize == 8)
				return reader.ReadInt64 ();
			else
				throw new TargetMemoryException (
					"Unknown target long integer size " + TargetLongIntegerSize);
		}

		long do_read_address ()
		{
			if (TargetAddressSize == 4)
				return (uint) reader.ReadInt32 ();
			else if (TargetAddressSize == 8)
				return reader.ReadInt64 ();
			else
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
		}

		public TargetAddress ReadAddress ()
		{
			long address = do_read_address ();

			if (address == 0)
				return TargetAddress.Null;
			else
				return new TargetAddress (info.AddressDomain, address);
		}

		public override string ToString ()
		{
			return String.Format ("MemoryReader ([{0}])", TargetBinaryReader.HexDump (data));
		}
	}
}
