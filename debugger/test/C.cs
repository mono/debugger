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

public class Bar<U> : Foo<U>
{
	public Bar (U u)
		: base (u)
	{ }
}

class X
{
	static void Main ()
	{
		Bar<int> bar = new Bar<int> (5);
		bar.Hello ();
	}
}
