using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This is a location in the client address space.
	// </summary>
	internal class ClientSuppliedTargetLocation : TargetLocation
	{
		TargetBlob blob;

		public ClientSuppliedTargetLocation (TargetBlob blob)
		{
			this.blob = blob;
		}

		public override bool HasAddress {
			get { return false; }
		}

		public override TargetAddress Address {
			get { throw new InvalidOperationException (); }
		}

		internal override TargetBlob ReadMemory (Thread target, int size)
		{
			if (size > blob.Size)
				throw new ArgumentException ();

			byte[] data = new byte [size];
			Array.Copy (blob.Contents, 0, data, 0, size);

			return new TargetBlob (data, blob.TargetInfo);
		}

		internal override void WriteBuffer (Thread target, byte[] data)
		{
			if (data.Length > blob.Size)
				throw new ArgumentException ();

			data.CopyTo (blob.Contents, 0);
		}

		internal override void WriteAddress (Thread target, TargetAddress new_address)
		{
			throw new InvalidOperationException ();
		}

		public override string Print ()
		{
			return TargetBinaryReader.HexDump (blob.Contents);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}", blob.Size);
		}
	}
}
