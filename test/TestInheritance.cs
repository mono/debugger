using System;

public struct A
{
	public int a;

	public A (int a)
	{
		this.a = a;
	}

	public void Test ()
	{
		Console.WriteLine (a);
	}

	public override string ToString ()
	{
		return a.ToString ();
	}
}

public class B
{
	public int a;

	public B (int a)
	{
		this.a = a;
	}

	public void Test ()
	{
		Console.WriteLine (a);
	}
}

public class C : B
{
	public float f;

	public C (int a, float f)
		: base (a)
	{
		this.f = f;
	}

	public void Hello ()
	{
		Test ();
		Console.WriteLine (f);
	}

	public virtual int Virtual ()
	{
		return 1;
	}
}

public class D : C
{
	public long e;

	public D (int a, float f, long e)
		: base (a, f)
	{
		this.e = e;
	}

	public override int Virtual ()
	{
		return 2;
	}
}

public class X
{
	public static void Main ()
	{
		A a = new A (5);
		a.Test ();

		DateTime time = DateTime.Now;
		Console.WriteLine (time);

		D d = new D (8, 3.14F, 500L);
		d.Hello ();
	}
}
