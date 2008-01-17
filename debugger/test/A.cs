using System;

public class Foo<T>
{
	public readonly T Data;

	public Foo (T t)
	{
		this.Data = t;
	}

	public void Hello ()
	{
		Console.WriteLine (Data);
	}
}

class X
{
	static void Main ()
	{
		Foo<int> foo = new Foo<int> (5);
		foo.Hello ();
	}
}
