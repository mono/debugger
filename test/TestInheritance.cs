using System;

public struct A
{
	public int a;
	public string Hello;
	public static string Boston = "Boston";

	public A (int a)
	{
		this.a = a;
		this.Hello = "Hello World";
	}

	public string Test ()
	{
		return Hello;
	}

	public static string StaticTest ()
	{
		return Boston;
	}

	public string Property {
		get {
			return Hello;
		}
	}

	public static string StaticProperty {
		get {
			return Boston;
		}
	}

	public override string ToString ()
	{
		return a.ToString ();
	}
}

public class B
{
	public int a;
	public string Hello;
	public static string Boston = "Boston";

	public B (int a)
	{
		this.a = a;
		this.Hello = "Hello World";
	}

	public string Test ()
	{
		return Hello;
	}

	public static string StaticTest ()
	{
		return Boston;
	}

	public string Property {
		get {
			return Hello;
		}
	}

	public static string StaticProperty {
		get {
			return Boston;
		}
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

	public new void Hello ()
	{
		Test ();
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

public abstract class AbstractTest
{
	public abstract string Test ();
}

public class AbstractHello : AbstractTest
{
	public override string Test ()
	{
		return "Hello";
	}
}

public class AbstractWorld : AbstractTest
{
	public override string Test ()
	{
		return "World";
	}
}

public class X
{
	public static void Main ()
	{
		A a = new A (5);
		a.Test ();

		D d = new D (8, 3.14F, 500L);
		d.Hello ();

		C c = d;
		c.Virtual ();

		AbstractTest hello = new AbstractHello ();
		AbstractTest world = new AbstractWorld ();

		Console.WriteLine (hello.Test ());
		Console.WriteLine (world.Test ());
		Console.WriteLine (c.f);
		d.Hello ();
	}
}
