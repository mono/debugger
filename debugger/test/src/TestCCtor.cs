using System;

public class Foo
{
	public void Hello ()
	{
		Console.WriteLine ("Hello World!");
		Console.WriteLine ("Second line.");
	}
}

public class Bar
{
	static Bar ()
	{
		Console.WriteLine ("BAR STATIC CCTOR!");
	}

	public static void Hello ()
	{
		Console.WriteLine ("Irish Pub");
	}
}

class X
{
	static X ()
	{
		Console.WriteLine ("X STATIC CCTOR!");
	}

	static void Test ()
	{
		Foo foo = new Foo ();
		foo.Hello ();
		foo.Hello ();
	}

	static void Main ()
	{
		Test ();
		Bar.Hello ();
	}
}
