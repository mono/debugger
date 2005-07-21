using System;

namespace Mono.Debugger.Languages.Mono
{
	//
	// FIXME: This class is just a big hack.  It's used when allocating objects to pass them to
	//        mono_runtime_invoke() or when creating a new instance of an object to assign it to
	//        a variable.
	//
	internal class Heap
	{
		public readonly ITargetInfo TargetInfo;
		public readonly TargetAddress StartAddress;
		public readonly int Size;

		public Heap (ITargetInfo info, TargetAddress start, int size)
		{
			this.TargetInfo = info;
			this.StartAddress = start;
			this.Size = size;
		}

		int end;

		public TargetLocation Allocate (StackFrame frame, int size)
		{
			if (end + size >= Size)
				throw new StackOverflowException ();

			TargetAddress start = StartAddress + end;
			end += size;
			return new HeapLocation (frame, this, start, start, size);
		}

		protected class HeapLocation : TargetLocation
		{
			Heap heap;
			TargetAddress base_address;
			TargetAddress address;
			int size;

			public HeapLocation (StackFrame frame, Heap heap, TargetAddress base_address,
					     TargetAddress address, int size)
				: base (frame, false, 0)
			{
				this.heap = heap;
				this.base_address = base_address;
				this.address = address;
				this.size = size;
			}

			public override bool HasAddress {
				get { return true; }
			}

			protected override TargetAddress GetAddress ()
			{
				return address;
			}

			protected override TargetLocation Clone (long offset)
			{
				return new HeapLocation (frame, heap, base_address, address + offset, size);
			}

			protected override void OnLocationInvalidEvent ()
			{
				base.OnLocationInvalidEvent ();
				// FIXME: Release object.
			}

			public override string Print ()
			{
				return String.Format ("Heap [{0}:{1}]", base_address, address);
			}

			protected override string MyToString ()
			{
				return String.Format (":{0}:{1}:{2}", base_address, address, size);
			}
		}
	}
}
