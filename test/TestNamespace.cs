using System;

namespace Martin
{
	namespace Baulig
	{
		public class Hello
		{
			public static void World ()
			{
				string text = Foo.Print ();
				Console.WriteLine (text);
			}
		}

		public class Foo
		{
			public static string Boston = "Boston";

			public static string Print ()
			{
				return Boston;
			}
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
