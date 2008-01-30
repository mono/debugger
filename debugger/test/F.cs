using System;

public class Foo<T>
{
	public readonly T Data;

	public Foo (T t)
	{
		this.Data = t;
	}

	public T Test {
		get { return Data; }
	}

	public void Hello ()
	{
		Console.WriteLine (Data);
	}
}

public class Test
{
	public static int StaticField;
	public int InstanceField;

	public static string Hello {
		get { return "Hello World"; }
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
	}
}
