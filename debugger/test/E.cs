using System;

public class Foo<S>
{
	public readonly S Data;

	public Foo (S s)
	{
		this.Data = s;
	}

	public void Hello ()
	{
		Console.WriteLine (Data);
	}
}

public class Test : Foo<int>
{
	public Test ()
		: base (9)
	{ }
}

class X
{
	static void Main ()
	{
		Test test = new Test ();
		test.Hello ();
	}
}
