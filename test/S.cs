using System;

class X
{
	static int total;

	static bool Test ()
	{
		return total < 100;
	}

	static object Total {
		get {
			return total;
		}
	}

	static void Main ()
	{
		total = 5;

		while (true)
			total += (int) Total;

		Console.WriteLine (total);
	}
}
