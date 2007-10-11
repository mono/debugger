using System;

public class Foo
{
	public static void Hello<S> (S s)
	{
		Console.WriteLine (s);
	}

	public static void Hello<T,U> (T t, U u)
	{
		Console.WriteLine ("TEST: {0} {1}", t, u);
	}

}

class X
{
	static void Main ()
	{
		Foo.Hello (5);
		Foo.Hello ("World");

		Foo.Hello (8);
		Foo.Hello ("Test");

		Foo.Hello (5, "World");
		Foo.Hello ("Test", 8);
	}
}
