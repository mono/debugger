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

		internal override bool HasAddress {
			get { return false; }
		}

		internal override TargetAddress GetAddress (TargetMemoryAccess target)
		{
			throw new InvalidOperationException ();
		}

		internal override TargetBlob ReadMemory (TargetMemoryAccess target, int size)
		{
			if (size > blob.Size)
				throw new ArgumentException ();

			byte[] data = new byte [size];
			Array.Copy (blob.Contents, 0, data, 0, size);

			return new TargetBlob (data, blob.TargetInfo);
		}

		internal override void WriteBuffer (TargetAccess target, byte[] data)
		{
			if (data.Length > blob.Size)
				throw new ArgumentException ();

			data.CopyTo (blob.Contents, 0);
		}

		internal override void WriteAddress (TargetAccess target, TargetAddress new_address)
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
