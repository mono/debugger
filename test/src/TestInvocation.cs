using System;

public class X
{
	public int Foo (int a)
	{
		Console.WriteLine ("Foo: {0}", a);
		return a * 2;
	}

	public string Foo (string b)
	{
		Console.WriteLine ("Foo with a string: {0}", b);
		return "Returned " + b;
	}

	public int Hello (int a)
	{
		Console.WriteLine ("Hello: {0}", a);
		return a * 4;
	}

	public static int StaticHello (int a)
	{
		Console.WriteLine ("Static Hello: {0}", a);
		return a * 8;
	}

	public void Test (int a, string b)
	{
		Foo (a);				// @MDB BREAKPOINT: test
		Foo (b);
		Hello (a);
		StaticHello (a);
	}

	public static void TestStatic (X y, int a, string b)
	{
		y.Foo (a);				// @MDB BREAKPOINT: test static
		y.Foo (b);
		y.Hello (a);
		StaticHello (a);
	}

	public static void BreakpointTest ()
	{ }						// @MDB LINE: breakpoint test

	public static void Main ()
	{
		X x = new X ();				// @MDB LINE: main
		x.Test (5, "Hello World");
		TestStatic (x, 9, "Boston");
		StaticHello (9);
		BreakpointTest ();			// @MDB BREAKPOINT: main2
	}
}
