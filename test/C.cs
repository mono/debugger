using System;

class X
{
	static void Test (params string[] args)
	{
		foreach (string arg in args)
			Console.WriteLine (arg);
		Console.WriteLine ("Done");
	}

	static long Test (params int[] args)
	{
		long sum = 0;
		foreach (int arg in args) {
			Console.WriteLine (arg);
			sum += arg;
		}
		Console.WriteLine ("Done");
		return sum;
	}

	static void Main ()
	{
		Test ("New York", "Boston", "Ximian");
		Console.WriteLine (Test (1,2,3,4,5,6,7,8));
	}
}
