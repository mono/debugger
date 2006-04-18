using System;
using System.IO;
using System.Text;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Backends
{
	[Serializable]
	internal class TargetInfo : ITargetInfo
	{
		int target_int_size;
		int target_long_size;
		int target_address_size;
		bool is_bigendian;
		AddressDomain address_domain;

		internal TargetInfo (int target_int_size, int target_long_size,
				     int target_address_size, bool is_bigendian,
				     AddressDomain domain)
		{
			this.target_int_size = target_int_size;
			this.target_long_size = target_long_size;
			this.target_address_size = target_address_size;
			this.is_bigendian = is_bigendian;
			this.address_domain = domain;
		}

		public int TargetIntegerSize {
			get {
				return target_int_size;
			}
		}

		public int TargetLongIntegerSize {
			get {
				return target_long_size;
			}
		}

		public int TargetAddressSize {
			get {
				return target_address_size;
			}
		}

		public bool IsBigEndian {
			get {
				return is_bigendian;
			}
		}

		public AddressDomain AddressDomain {
			get {
				return address_domain;
			}
		}
	}

	[Serializable]
	internal class TargetReader
	{
		byte[] data;
		TargetBinaryReader reader;
		ITargetInfo info;

		internal TargetReader (byte[] data, ITargetInfo info)
		{
			if ((info == null) || (data == null))
				throw new ArgumentNullException ();
			this.reader = new TargetBinaryReader (data, info);
			this.info = info;
			this.data = data;
		}

		internal TargetReader (TargetBlob data, ITargetInfo info)
			: this (data.Contents, info)
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
