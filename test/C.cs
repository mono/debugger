using System;

class X
{
	static void Test (params string[] args)
	{
		foreach (string arg in args)
			Console.WriteLine (arg);
		Console.WriteLine ("Done");
	}

	static void Main ()
	{
		Test ("New York", "Boston", "Ximian");
	}
}
