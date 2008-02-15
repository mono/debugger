using System;

delegate void S ();

public class Foo<T>
{
	public readonly T Data;

	public Foo (T t)
	{
		this.Data = t;
	}

	public void Test ()
	{
		T t = Data;
		Console.WriteLine (t);

		S b = delegate {
			Console.WriteLine (t);
		};

		b ();
	}
}

class X
{
	static void Main ()
	{
		Foo<int> foo = new Foo<int> (3);
		foo.Test ();
	}
}
