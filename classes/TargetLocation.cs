using System;
using System.Text;

namespace Mono.Debugger
{
	public abstract class TargetLocation : ITargetLocation
	{
		TargetAddress address;
		long offset;

		internal TargetLocation (TargetAddress address, long offset)
		{
			this.address = address;
			this.offset = offset;
		}

		public abstract TargetAddress Address {
			get;
		}

		public abstract bool HasAddress {
			get;
		}

		public long Offset {
			get {
				return offset;
			}

			set {
				offset = value;
			}
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();

			builder.Append (address);
			if (offset > 0) {
				builder.Append ("+0x");
				builder.Append (offset.ToString ("x"));
			}

			return builder.ToString ();
		}

		public abstract object Clone ();
	}
}
