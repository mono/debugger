using System;
using System.IO;
using System.Text;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Backends
{
	internal class TargetInfo : ITargetInfo
	{
		int target_int_size;
		int target_long_size;
		int target_address_size;

		internal TargetInfo (int target_address_size)
			: this (4, 8, target_address_size)
		{ }

		internal TargetInfo (int target_int_size, int target_long_size,
				     int target_address_size)
		{
			this.target_int_size = target_int_size;
			this.target_long_size = target_long_size;
			this.target_address_size = target_address_size;
		}

		int ITargetInfo.TargetIntegerSize {
			get {
				return target_int_size;
			}
		}

		int ITargetInfo.TargetLongIntegerSize {
			get {
				return target_long_size;
			}
		}

		int ITargetInfo.TargetAddressSize {
			get {
				return target_address_size;
			}
		}
	}

	internal class TargetReader : ITargetMemoryReader
	{
		byte[] data;
		TargetBinaryReader reader;
		int offset;
		IInferior inferior;

		internal TargetReader (byte[] data, IInferior inferior)
		{
			this.reader = new TargetBinaryReader (data, inferior);
			this.inferior = inferior;
			this.data = data;
			this.offset = 0;
		}

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
				return inferior.TargetIntegerSize;
			}
		}

		public int TargetLongIntegerSize {
			get {
				return inferior.TargetLongIntegerSize;
			}
		}

		public int TargetAddressSize {
			get {
				return inferior.TargetAddressSize;
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
			return reader.ReadInt64 ();
		}

		public TargetAddress ReadAddress ()
		{
			if (TargetAddressSize == 4)
				return new TargetAddress (inferior, reader.ReadInt32 ());
			else if (TargetAddressSize == 8)
				return new TargetAddress (inferior, reader.ReadInt64 ());
			else
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
		}

		public override string ToString ()
		{
			StringBuilder sb = new StringBuilder ();

			sb.Append (String.Format ("MemoryReader (["));
			for (int i = 0; i < data.Length; i++) {
				if (i > 0)
					sb.Append (" ");
				sb.Append (String.Format ("{1}{0:x}", data [i], data [i] >= 16 ? "" : "0"));
			}
			sb.Append ("])");
			return sb.ToString ();
		}
	}
}
