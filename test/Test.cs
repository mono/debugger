using System;

class X
{
	int Test (ref long a, bool b, int c, short d, float f)
	{
		Console.WriteLine ("VALUE: {0}", a);
		return c;
	}

	static void Main ()
	{
		X x = new X ();

		long b = 29;
		int a = x.Test (ref b, true, 59, -18, 3.14F);
		Console.WriteLine (a);
	}
}
