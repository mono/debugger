using System;

class X
{
	static int Test (int a, int b, float f)
	{
		return a + 5 * b;
	}

	static void Main ()
	{
		int a = 5;
		int b = 29;
		float f = 3.14F;

		int d = Test (a, b, f);

		Console.WriteLine (a);
		Console.WriteLine (b);
		Console.WriteLine (d);
		Console.WriteLine (f);
	}
}
