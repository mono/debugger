using System;

public class Foo
{
	public void Hello ()
	{
		Console.WriteLine ("Hello World!");
		Console.WriteLine ("Second line.");
	}
}

class X
{
	static X ()
	{
		Console.WriteLine ("STATIC CCTOR!");
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
	}
}
