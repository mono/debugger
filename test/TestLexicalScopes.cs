using System;

class X
{
	static int Main ()
	{
		Console.WriteLine ("Begin");

		{
			int a = 5;
			Console.WriteLine (a);
		}

		Console.WriteLine ("Middle");

		{
			float a = 8.9F;
			Console.WriteLine (a);

			{
				long b = 7;
				Console.WriteLine (b);
			}

			Console.WriteLine (a);
		}

		Console.WriteLine ("End");
		return 0;
	}
}
