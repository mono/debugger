using System;

public class Foo<T>
{
	public T Data;

	public void Hello ()
	{
		Console.WriteLine (Data);
	}
}

public class Test : Foo<Test>
{
	public Test ()
	{
		Data = this;
	}
}

class X
{
	static void Main ()
	{
		Test test = new Test ();
		test.Hello ();
	}
}
