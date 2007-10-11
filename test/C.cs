using System;

public struct Foo
{
	public readonly int A;

	public Foo (int a)
	{
		this.A = a;
	}
}

public class Bar
{
	public readonly int A;

	public Bar (int a)
	{
		this.A = a;
	}
}

public enum Drinks
{
	Beer,
	Whine
}

public class Hello<T>
{
	public readonly T Data;

	public Hello (T t)
	{
		this.Data = t;
	}

	public void Test ()
	{
		Console.WriteLine (Data);
	}
}

public class Test
{
	public static void Hello<S> (S s)
	{
		Console.WriteLine (s);
	}
}

class X
{
	static void Main ()
	{
		Hello<int> hello = new Hello<int> (3);
		hello.Test ();
		Test.Hello (hello);

		Foo foo = new Foo (5);
		Bar bar = new Bar (8);

		Test.Hello (foo);
		Test.Hello (bar);

		Test.Hello ((object) bar);

		int[] a = { 3, 8 };
		Test.Hello (a);

		int[,] b = { { 3, 8, 11 }, { 5, 9, 7 } };
		Test.Hello (b);

		Test.Hello (IntPtr.Zero);
		Test.Hello (Drinks.Beer);
	}
}
