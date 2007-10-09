using System;

public class Foo<T>
{
	public readonly int A;
	public readonly T Data;

	public Foo (int a, T t)
	{
		this.A = a;
		this.Data = t;
	}

	public void Hello (T t)
	{
		Console.WriteLine (t);
	}
}

class X
{
	static void Main ()
	{
		Foo<long> foo = new Foo<long> (5, 81);
		foo.Hello (5);
	}
}
