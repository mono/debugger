using System;

class X
{
	enum Foo : long {
		A = 5,
		B,
		C = 512
	}

	int Test (ref long a, bool b, int c, short d, float f, DateTime time, Foo g)
	{
		Console.WriteLine ("VALUE: {0}", a);
		return c;
	}

	static long ArrayTest (int[,,] a, long[] b, string[,] s)
	{
		return 29;
	}

	static void Main (string[] argv)
	{
		X x = new X ();

		int[,,] a = { { {  5,  6,  7 }, {  8,  2,  4}, {  6,  1,  9 }, {  10,  50,  200 } },
			      { { -5, -6, -7 }, { -8, -2, -4}, { -6, -1, -9 }, { -10, -50, -200 } } };
		long[] b = { 59, 8, -19 };

		string[,] test = { { "Hello", "World" }, { "Ximian", "Monkeys" } };

		long c = ArrayTest (a, b, test);
		DateTime time = DateTime.Now;
		int d = x.Test (ref c, true, 59, -18, 3.14F, time, Foo.B);
		Console.WriteLine (d);
	}
}
