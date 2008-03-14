using System;

public delegate void Foo (long a);

class Test
{
	public static void Hello (int t)
	{
		Foo foo = delegate (long r) {
			Console.WriteLine (r);
		};
		foo (5);
	}
}

class X
{
	static void Main ()
	{
		Test.Hello (9);
	}
}
