using System;

class X
{
	void Test ()
	{
		Console.WriteLine ("Test");
	}

	static void CrashHere (X x, int a)
	{
		Console.WriteLine ("CRASHING HERE: {0}", a);
		x.Test ();
	}

	static void Foo ()
	{
		long b = 29;
		CrashHere (null, 29);
	}

	static void Main ()
	{
		Foo ();
	}
}
