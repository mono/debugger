using System;

namespace Martin
{
	namespace Baulig
	{
		public class Hello
		{
			public static void World ()
			{ }
		}
	}
}

namespace Test
{
	using Martin.Baulig;

	class X
	{
		static void Main ()
		{
			Hello.World ();
		}
	}
}
