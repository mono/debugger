using System;
using System.Runtime.CompilerServices;

namespace Mono.Debugger.Tests
{
	class TestInternCall
	{
		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		public extern static void Test ();

		static void Main ()
		{
			Test ();
		}
	}
}

