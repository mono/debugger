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
		Foo (a);
		Foo (b);
		Hello (a);
		StaticHello (a);
	}

	public static void Main ()
	{
		X x = new X ();
		x.Test (5, "Hello World");
		StaticHello (9);
	}
}
