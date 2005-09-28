using System;

class X
{
	public static void Test (out int foo)
	{
		foo = 3;
		Console.WriteLine (foo);
	}

	public static unsafe void UnsafeTest (int foo)
	{
		int* ptr = &foo;
		Console.WriteLine (*ptr);
	}

	static void Main ()
	{
		int foo;
		Test (out foo);
		UnsafeTest (foo);
	}
}
