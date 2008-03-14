using System;

delegate void S ();

class X
{
	static int Main ()
	{
		int a = 1;

		S b = delegate {
			a = 2;
		};

		Console.WriteLine (a);

		b ();

		return 0;
	}
}
