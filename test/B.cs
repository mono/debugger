using System;

class X
{
	static void Test (int a)
	{
		long b = 29;

		if (a == 5)
			Console.WriteLine ("Hello World!");
		else
			b = 17;

		if (a == 8) {
			Console.WriteLine ("New York");
		} else if (a == 7) {
			Console.WriteLine ("Boston");
			if (b == 17)
				Console.WriteLine ("Monkey Party!");
		}
	}

	static void Main ()
	{
		Test (5);
		Test (8);
		Test (7);
	}
}
