using System;

public class Foo<T>
{
	public readonly T Data;

	public Foo (T t)
	{
		this.Data = t;
	}

	protected Foo ()
	{ }

	public T Test {
		get { return Data; }
	}

	public T Hello ()
	{
		return Data;
	}

	public void Broken (T Data)
	{ }

	public void NotSoBroken (int a)
	{
		Console.WriteLine (a);
	}
}

public class Test : Foo<Test>
{
	public static int StaticField;
	public int InstanceField;

	public Test ()
	{ }

	public void Method (int a)
	{
		Console.WriteLine (a);
	}
}

class X
{
	static void Main ()
	{
		Foo<int> foo = new Foo<int> (5);
		foo.Hello ();
		foo.Broken (9);
		foo.NotSoBroken (88);

		Test test = new Test ();
		test.InstanceField = 5;
	}
}
