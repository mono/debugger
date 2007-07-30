using System;

namespace Mono.Debugger.Languages
{
	internal class DereferencedTargetLocation : TargetLocation
	{
		TargetLocation reference;

		public DereferencedTargetLocation (TargetLocation reference)
		{
			this.reference = reference;
		}

		public override bool HasAddress {
			get { return false; }
		}

		public override TargetAddress Address {
			get { throw new InvalidOperationException (); }
		}

		internal override TargetBlob ReadMemory (Thread target, int size)
		{
			TargetAddress address = target.ReadAddress (reference.Address);
			return target.ReadMemory (address, size);
		}

		internal override void WriteBuffer (Thread target, byte[] data)
		{
			TargetAddress address = target.ReadAddress (reference.Address);
			target.WriteBuffer (address, data);
		}

		internal override void WriteAddress (Thread target, TargetAddress address)
		{
			reference.WriteAddress (target, address);
		}

		public override string Print ()
		{
			return String.Format ("*{0}", reference.Address);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}", reference);
		}
	}
}
