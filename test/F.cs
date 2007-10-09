using System;

public class Foo<S>
{
	public readonly int A;
	public readonly S Data;

	public Foo (int a, S s)
	{
		this.A = a;
		this.Data = s;
	}

	public void Hello (S s)
	{
		Console.WriteLine (s);
	}
}

public class Bar<T,U> : Foo<U>
{
	public readonly T New;

	public Bar (int a, T t, U u)
		: base (a, u)
	{
		this.New = t;
	}
}

class X
{
	static void Main ()
	{
		Foo<long> foo = new Foo<long> (5, 81);
		foo.Hello (5);

		Bar<float,int> bar = new Bar<float,int> (8, 3.14F, 16384);
		bar.Hello (32);
	}
}
