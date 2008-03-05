using System;

public class Foo<T>
{
	public T Data;

	public Foo (T t)
	{
		this.Data = t;
	}

	public T Test {
		get { return Data; }
		set { Data = value; }
	}

	public void Hello ()
	{
		Console.WriteLine (Data);
	}

	public T[] World (T t)
	{
		Console.WriteLine (t);
		return new T [] { t };
	}

	public void Generic<S> (S s)
	{
		Console.WriteLine (s);
	}
}

public class Test
{
	public static int StaticField;
	public int InstanceField;

	public static string Hello {
		get { return "Hello World"; }
	}

	public int Foo {
		get; set;
	}
}

class X
{
	static void Main ()
	{
		Foo<int> foo = new Foo<int> (5);
		foo.Hello ();

		Test test = new Test ();
		test.InstanceField = 5;
		test.Foo = 9;

		Console.WriteLine (test);
	}
}
