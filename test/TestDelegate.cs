using System;

public delegate void FooHandler ();

class X
{
	public event FooHandler Foo;

	public X ()
	{
		Foo += new FooHandler (foo);
		Foo += new FooHandler (boston);
	}

	void foo ()
	{
		Console.WriteLine ("Hello World");
	}

	void boston ()
	{
		Console.WriteLine ("Boston");
	}

	static void Main ()
	{
		X x = new X ();
		x.Foo ();
	}
}
