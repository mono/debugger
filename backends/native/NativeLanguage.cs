using System;
using System.Runtime.InteropServices;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeLanguage : ILanguage
	{
		NativeFundamentalType integer_type;
		NativeFundamentalType long_type;
		NativeOpaqueType pointer_type;

		public NativeLanguage ()
		{
			integer_type = new NativeFundamentalType ("int", typeof (int), Marshal.SizeOf (typeof (int)));
			long_type = new NativeFundamentalType ("long", typeof (long), Marshal.SizeOf (typeof (long)));
			pointer_type = new NativeOpaqueType ("pointer", 0);
		}

		public string Name {
			get { return "native"; }
		}

		public ITargetFundamentalType IntegerType {
			get { return integer_type; }
		}

		public ITargetFundamentalType LongIntegerType {
			get { return long_type; }
		}

		public ITargetPointerType PointerType {
			get { return pointer_type; }
		}
	}
}
