using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Mono.Debugger.Tests
{
	class TestInternCall
	{
		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		public extern static void Test ();

		[DllImport("libm.so.6")]
		public extern static double asin (double x);

		static void Main ()
		{
			Test ();

			double y = asin (1.0);
			Console.WriteLine (y);
		}
	}
}

