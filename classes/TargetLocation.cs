using System;
using System.Text;

namespace Mono.Debugger
{
	public class TargetLocation : ITargetLocation
	{
		long addr;

		public TargetLocation (long addr)
		{
			this.addr = addr;
		}

		public long Location {
			get {
				return addr;
			}
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();

			if (addr > 0) {
				builder.Append ("0x");
				builder.Append (addr.ToString ("x"));
			} else
				builder.Append ("<unknown>");

			return builder.ToString ();
		}
	}
}
