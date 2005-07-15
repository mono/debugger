using System;

namespace Mono.Debugger
{
	// <summary>
	//   This is used to store an address in the target's address space.
	//   Addresses are only valid in the target that was used to get them.
	//
	//   You should always use this struct to pass addresses around, it is a
	//   valuetype and has operators to compare, add, increment, decrement
	//   addresses.
	//
	//   Note that it is not allowed to compare addresses from different
	//   targets, you'll get an ArgumentException() if you attempt to do so.
	//   This is also the reason why you should never use the `Address' property
	//   unless you know exactly what you're doing: by using this whole valuetype
	//   to pass addresses around, it'll protect you against accidentally using
	//   an address in another target.
	// </summary>
	[Serializable]
	public struct TargetAddress : IComparable
	{
		private ulong address;
		private AddressDomain domain;

		// <remarks>
		//   Don't use this in a comparision, use IsNull instead.
		//   (TargetAddress.Null is in the null domain and comparision between
		//    different domains is not allowed).
		// </remarks>
		public static TargetAddress Null = new TargetAddress (null, 0);

		// <summary>
		//   This is not what it looks like.
		//   Never use this constructor unless you know exactly what you're doing.
		// </summary>
		public TargetAddress (AddressDomain domain, long address)
		{
			this.domain = domain;
			// FIXME: hack
			// address &= 0x00000000ffffffffL;
			this.address = (ulong) address;
		}

		public TargetAddress (AddressDomain domain, Register register)
			: this (domain, register.Value)
		{
		}

		// <summary>
		//   This is not what it looks like.
		//   Never use this property unless you know exactly what you're doing.
		// </summary>
		public long Address {
			get {
				return (long) address;
			}
		}

		// <summary>
		//   The `domain' in which this address lives.
		//   Normally, this is in instance of the IInferior object, but it can also
		//   be anything else.  It is used to ensure that addresses are only used in
		//   the domain they came from.
		// </summary>
		public AddressDomain Domain {
			get {
				return domain;
			}
		}

		// <summary>
		//   Use this to check whether an address is null.
		// </summary>
		public bool IsNull {
			get {
				return (address == 0) && (domain == null);
			}
		}

		public override string ToString ()
		{
			return String.Format ("0x{0}", FormatAddress ((long) address));
		}

		public static string FormatAddress (long address)
		{
			int bits = 8;
			string saddr = address.ToString ("x");
			for (int i = saddr.Length; i < bits; i++)
				saddr = "0" + saddr;
			return saddr;
		}

		static void check_domains (TargetAddress a, TargetAddress b)
		{
			if (a.domain == b.domain)
				return;

			throw new ArgumentException (String.Format (
				"Cannot compare addresses from different domains {0} and {1}",
				a.Domain, b.Domain));
		}

		public int CompareTo (object obj)
		{
			TargetAddress addr = (TargetAddress) obj;

			check_domains (addr, this);

			if (address < addr.address)
				return -1;
			else if (address > addr.address)
				return 1;
			else
				return 0;
		}

		public override bool Equals (object o)
		{
			if (o == null || !(o is TargetAddress))
				return false;

			TargetAddress b = (TargetAddress)o;
			return address == b.address;
		}

		public override int GetHashCode ()
		{
			return (int)address;
		}

		//
		// Operators
		//

		public static bool operator < (TargetAddress a, TargetAddress b)
		{
			check_domains (a, b);
			return a.address < b.address;
		}

		public static bool operator > (TargetAddress a, TargetAddress b)
		{
			check_domains (a, b);
			return a.address > b.address;
		}

		public static bool operator == (TargetAddress a, TargetAddress b)
		{
			check_domains (a, b);
			return a.Equals (b);
		}

		public static bool operator != (TargetAddress a, TargetAddress b)
		{
			check_domains (a, b);
			return a.address != b.address;
		}

		public static bool operator <= (TargetAddress a, TargetAddress b)
		{
			check_domains (a, b);
			return a.address <= b.address;
		}

		public static bool operator >= (TargetAddress a, TargetAddress b)
		{
			check_domains (a, b);
			return a.address >= b.address;
		}

		public static TargetAddress operator + (TargetAddress a, long offset)
		{
			return new TargetAddress (a.domain, (long) (a.address + (ulong) offset));
		}

		public static TargetAddress operator - (TargetAddress a, long offset)
		{
			return new TargetAddress (a.domain, (long) (a.address - (ulong) offset));
		}

		public static TargetAddress operator ++ (TargetAddress a)
		{
			a.address++;
			return a;
		}

		public static TargetAddress operator -- (TargetAddress a)
		{
			a.address--;
			return a;
		}

		public static long operator - (TargetAddress a, TargetAddress b)
		{
			check_domains (a, b);

			if (a > b)
				return (long) (a.address - b.address);
			else
				return - (long) (b.address - a.address);
		}
	}
}
