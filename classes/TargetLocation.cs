using System;
using System.Text;

namespace Mono.Debugger
{
	public class TargetLocation : ITargetLocation
	{
		long addr;
		int offset;

		internal TargetLocation (long addr)
			: this (addr, 0)
		{ }

		internal TargetLocation (long addr, int offset)
		{
			this.addr = addr;
			this.offset = offset;
		}

		public static ITargetLocation Null = new TargetLocation (0);

		public long Location {
			get {
				return addr;
			}
		}

		public int Offset {
			get {
				return offset;
			}

			set {
				offset = value;
			}
		}

		public long Address {
			get {
				return addr + offset;
			}
		}

		public bool IsNull {
			get {
				return addr == 0;
			}
		}

		public int CompareTo (object obj)
		{
			ITargetLocation target = (ITargetLocation) obj;

			if (Address < target.Address)
				return -1;
			else if (Address >= target.Address)
				return 1;
			else
				return 0;
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();

			if (addr > 0) {
				builder.Append ("0x");
				builder.Append (addr.ToString ("x"));
			} else
				builder.Append ("<unknown>");
			if (offset > 0) {
				builder.Append ("+0x");
				builder.Append (offset.ToString ("x"));
			}

			return builder.ToString ();
		}

		public object Clone ()
		{
			return new TargetLocation (addr, offset);
		}
	}
}
