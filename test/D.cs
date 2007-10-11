using System;

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
	public static void Hello<S> (Hello<S> hello)
	{
		Console.WriteLine (hello);
	}
}

class X
{
	static void Main ()
	{
		Hello<int> hello = new Hello<int> (3);
		hello.Test ();
		Test.Hello (hello);
	}
}
