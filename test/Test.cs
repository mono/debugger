using System;

class X
{
	void Test (long a, bool b, int c, short d, float f)
	{
		Console.WriteLine ("VALUE: {0}", a);
	}

	static void Main ()
	{
		X x = new X ();

		long b = 29;
		x.Test (b, true, 59, -18, 3.14F);
	}
}
