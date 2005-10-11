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

		internal TargetInfo (int target_address_size)
			: this (4, 8, target_address_size, false)
		{ }

		internal TargetInfo (int target_int_size, int target_long_size,
				     int target_address_size, bool is_bigendian)
		{
			this.target_int_size = target_int_size;
			this.target_long_size = target_long_size;
			this.target_address_size = target_address_size;
			this.is_bigendian = is_bigendian;
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
	}

	[Serializable]
	internal class TargetMemoryInfo : TargetInfo, ITargetMemoryInfo
	{
		Architecture arch;
		AddressDomain address_domain;
		AddressDomain global_address_domain;

		internal TargetMemoryInfo (int target_int_size, int target_long_size,
					   int target_address_size, bool is_bigendian,
					   AddressDomain global_domain, AddressDomain domain)
			: base (target_int_size, target_long_size, target_address_size,
				is_bigendian)
		{
			this.global_address_domain = global_domain;
			this.address_domain = domain;
		}

		internal void Initialize (Architecture arch)
		{
			this.arch = arch;
		}

		public AddressDomain AddressDomain {
			get {
				return address_domain;
			}
		}

		public AddressDomain GlobalAddressDomain {
			get {
				return global_address_domain;
			}
		}

		public Architecture Architecture {
			get {
				return arch;
			}
		}
	}

	[Serializable]
	internal class TargetReader : ITargetMemoryReader
	{
		byte[] data;
		TargetBinaryReader reader;
		ITargetMemoryInfo info;

		internal TargetReader (byte[] data, ITargetMemoryInfo info)
		{
			this.reader = new TargetBinaryReader (data, info);
			this.info = info;
			this.data = data;
		}

		internal TargetReader (TargetBlob data, ITargetMemoryInfo info)
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

		public Architecture Architecture {
			get {
				return info.Architecture;
			}
		}

		public AddressDomain AddressDomain {
			get {
				return info.AddressDomain;
			}
		}

		public AddressDomain GlobalAddressDomain {
			get {
				return info.GlobalAddressDomain;
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

		public TargetAddress ReadGlobalAddress ()
		{
			long address = do_read_address ();

			if (address == 0)
				return TargetAddress.Null;
			else
				return new TargetAddress (info.GlobalAddressDomain, address);
		}

		public override string ToString ()
		{
			return String.Format ("MemoryReader ([{0}])", TargetBinaryReader.HexDump (data));
		}
	}
}
