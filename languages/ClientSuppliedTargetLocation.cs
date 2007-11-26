using System;

using Mono.Debugger.Backends;

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

		internal override TargetAddress GetAddress (InternalTargetAccess target)
		{
			throw new InvalidOperationException ();
		}

		internal override TargetBlob ReadMemory (InternalTargetAccess target, int size)
		{
			if (size > blob.Size)
				throw new ArgumentException ();

			byte[] data = new byte [size];
			Array.Copy (blob.Contents, 0, data, 0, size);

			return new TargetBlob (data, blob.TargetMemoryInfo);
		}

		internal override void WriteBuffer (InternalTargetAccess target, byte[] data)
		{
			if (data.Length > blob.Size)
				throw new ArgumentException ();

			data.CopyTo (blob.Contents, 0);
		}

		internal override void WriteAddress (InternalTargetAccess target,
						     TargetAddress new_address)
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
