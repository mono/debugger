using System;

namespace Mono.Debugger
{
	[Flags]
	public enum TargetMemoryFlags
	{
		ReadOnly	= 1
	}

	public sealed class TargetMemoryArea
	{
		ITargetMemoryAccess memory;
		TargetAddress start, end;
		TargetMemoryFlags flags;
		string name;

		internal TargetMemoryArea (TargetAddress start, TargetAddress end,
					   TargetMemoryFlags flags, string name,
					   ITargetMemoryAccess memory)
		{
			this.memory = memory;
			this.start = start;
			this.end = end;
			this.flags = flags;
			this.name = name;
		}

		public TargetAddress Start {
			get {
				return start;
			}
		}

		public TargetAddress End {
			get {
				return end;
			}
		}

		public TargetMemoryFlags Flags {
			get {
				return flags;
			}
		}

		public string Name {
			get {
				return name;
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0,10} {1,10} {2,8} {3}", start, end, flags, name);
		}
	}
}
