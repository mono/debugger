using System;

namespace Mono.Debugger
{
	public class TargetMemoryException : TargetException
	{
		public TargetMemoryException (string message)
			: base (message)
		{ }

		public TargetMemoryException (ITargetLocation address)
			: this (String.Format ("Cannot read target memory at address 0x{0:x}", address))
		{ }

		public TargetMemoryException (ITargetLocation address, int size)
			: this (String.Format ("Cannot read {1} bytes from target memory at address 0x{0:x}",
					       address, size))
		{ }
	}

	public class TargetMemoryReadOnlyException : TargetMemoryException
	{
		public TargetMemoryReadOnlyException ()
			: base ("The current target's memory is read-only")
		{ }

		public TargetMemoryReadOnlyException (ITargetLocation address)
			: base (String.Format ("Can't write to target memory at address 0x{0:x}: {1}",
					       address, "the current target's memory is read-only"))
		{ }
	}
}
