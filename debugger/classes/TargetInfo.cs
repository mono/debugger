using System;
using System.Text;

namespace Mono.Debugger
{
	[Serializable]
	public class TargetInfo
	{
		int target_int_size;
		int target_long_size;
		int target_address_size;
		bool is_bigendian;

		internal TargetInfo (int target_int_size, int target_long_size,
				     int target_address_size, bool is_bigendian)
		{
			this.target_int_size = target_int_size;
			this.target_long_size = target_long_size;
			this.target_address_size = target_address_size;
			this.is_bigendian = is_bigendian;
		}

		public int TargetIntegerSize {
			get {
				return target_int_size;
			}
		}

		public int TargetLongIntegerSize {
			get {
				return target_long_size;
			}
		}

		public int TargetAddressSize {
			get {
				return target_address_size;
			}
		}

		public bool IsBigEndian {
			get {
				return is_bigendian;
			}
		}
	}

	[Serializable]
	public class TargetMemoryInfo : TargetInfo
	{
		internal TargetMemoryInfo (int target_int_size, int target_long_size,
					   int target_address_size, bool is_bigendian,
					   AddressDomain domain)
			: base (target_int_size, target_long_size, target_address_size,
				is_bigendian)
		{
			this.address_domain = domain;
		}

		AddressDomain address_domain;


		public AddressDomain AddressDomain {
			get {
				return address_domain;
			}
		}
	}
}
