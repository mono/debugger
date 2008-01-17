using System;

public class Hello<S>
{
	public void World (S s)
	{
		Console.WriteLine (s);
	}
}

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

public class Bar<U> : Foo<Hello<U>>
{
	public Bar (U u)
		: base (new Hello<U> ())
	{ }
}

class X
{
	static void Main ()
	{
		Bar<int> bar = new Bar<int> (5);
		bar.Data.World (8);
		bar.Data.World (10);
		bar.Hello ();
	}
}
