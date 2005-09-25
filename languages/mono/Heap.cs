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

		public TargetAddress Allocate (TargetAccess target, int size)
		{
			if (end + size >= Size)
				throw new StackOverflowException ();

			TargetAddress start = StartAddress + end;
			end += size;
			return start;
		}
	}
}
