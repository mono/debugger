using System;

class X
{
	int Test (ref long a, bool b, int c, short d, float f)
	{
		Console.WriteLine ("VALUE: {0}", a);
		return c;
	}

	static long ArrayTest (int[,,] a)
	{
		return a [2,1,3];
	}

	static void Main (string[] argv)
	{
		X x = new X ();

		int[,,] a = { { {  5,  6,  7 }, {  8,  2,  4}, {  6,  1,  9 } },
			      { { -5, -6, -7 }, { -8, -2, -4}, { -6, -1, -9 } } };

		long b = ArrayTest (a);
		int c = x.Test (ref b, true, 59, -18, 3.14F);
		Console.WriteLine (c);
	}
}
