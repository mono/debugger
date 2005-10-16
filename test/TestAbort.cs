using System;
using System.Threading;

class X
{
	static void Test ()
	{
		Thread.CurrentThread.Abort ();
	}

	static void Hello ()
	{
		Console.WriteLine ("Hello World");
	}

	static int Count = 0;

	static int Hello (int a)
	{
		long b = 2 * a;
		try {
			Hello ();
		} finally {
			++Count;
			Console.WriteLine ("Done: {0} {1} {2}", a, b, Count);
		}

		return 1;
	}

	static void Main ()
	{
		Hello (5);
		Console.WriteLine (Count);
		Test ();
	}
}
