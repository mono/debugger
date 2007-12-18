using System;

public abstract class Foo
{
	public override string ToString ()
	{
		return "Hello World!";
	}
}

public class Bar : Foo
{
}

public class B
{
	public int a;
	public long b;
	public string c;

	public static float Test = 3.14F;

	public float Property {
		get { return Test; }
	}

	public B (int a, long b, string c)
	{
		this.a = a;
		this.b = b;
		this.c = c;
	}

	public void Foo ()
	{
		Console.WriteLine (a);
		Console.WriteLine (Test);
	}

	public void Foo (string hello)
	{
		Console.WriteLine (hello);
	}
}

public class C : B
{
	public C (int a, long b, string c)
		: base (a, b, c)
	{ }

	public override string ToString ()
	{
		return String.Format ("C ({0}:{1}:{2})", a, b, c);
	}
}

public struct Hello
{
	public readonly float PI;

	public Hello (float pi)
	{
		this.PI = pi;
	}

	public override string ToString ()
	{
		return "Albert Einstein";
	}
}

class X
{
	static void Test (Foo foo, Bar bar, Hello hello, B b, C c, B d)
	{ }

	static void Main ()
	{
		Foo foo = new Bar ();
		Bar bar = new Bar ();
		Hello hello = new Hello (3.1415F);
		B b = new B (5, 256, "Robbie Williams");
		C c = new C (5, 256, "Robbie Williams");
		B bb = (B) c;

		Test (foo, bar, hello, b, c, bb);
	}
}
