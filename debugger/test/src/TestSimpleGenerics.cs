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

public class Bar<U> : Foo<U>
{
	public Bar (U u)
		: base (u)
	{ }
}

public class Baz<U> : Foo<Hello<U>>
{
	public Baz (U u)
		: base (new Hello<U> ())
	{ }
}

public class Test : Foo<int>
{
	public Test ()
		: base (9)
	{ }

	public static void Hello<T> (T t)
	{
		Console.WriteLine (t);
	}
}

class X
{
	static void Main ()
	{
		Foo<int> foo = new Foo<int> (5);
		foo.Hello ();

		Bar<int> bar = new Bar<int> (5);
		bar.Hello ();

		Baz<int> baz = new Baz<int> (5);
		baz.Data.World (8);
		baz.Hello ();

		Test test = new Test ();
		test.Hello ();
		Test.Hello (8);
		Test.Hello ("World");
	}
}
