using System;

class X
{
	void Test (long a)
	{
		Console.WriteLine ("VALUE: {0}", a);
	}

	static void Main ()
	{
		X x = null;

		x.Test (5);
	}
}
