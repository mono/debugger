using System;

public class Foo
{
	public static void Hello<T> (T t)
	{
		Console.WriteLine (t);
	}
}

class X
{
	static void Main ()
	{
		Foo.Hello (8);
		Foo.Hello ("World");
	}
}
