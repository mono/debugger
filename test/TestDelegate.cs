using System;

public delegate long FooHandler (int a);

class X
{
	public event FooHandler Foo;

	public X ()
	{
		Foo += new FooHandler (foo);
		Foo += new FooHandler (boston);
	}

	long foo (int a)
	{
		Console.WriteLine ("Hello World: {0}", a);
		return 2 * a;
	}

	long boston (int a)
	{
		Console.WriteLine ("Boston: {0}", a);
		return 3 * a;
	}

	static void Main ()
	{
		X x = new X ();
		x.Foo (4);
		Console.WriteLine ("Back in main");
		x.Foo (11);
	}
}
