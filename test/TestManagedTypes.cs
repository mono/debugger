using System;

struct A
{
	public int a;
	public long b;
	public string c;

	public A (int a, long b, string c)
	{
		this.a = a;
		this.b = b;
		this.c = c;
	}
}

class B
{
	public int a;
	public long b;
	public string c;

	public B (int a, long b, string c)
	{
		this.a = a;
		this.b = b;
		this.c = c;
	}
}

class C : B
{
	public new int a;
	public float f;

	public C (int a, long b, string c, float f, int new_a)
		: base (a, b, c)
	{
		this.f = f;
		this.a = new_a;
	}
}

struct D
{
	public A a;
	public B b;
	public C c;
	public string[] s;

	public D (A a, B b, C c, params string[] args)
	{
		this.a = a;
		this.b = b;
		this.c = c;
		this.s = args;
	}
}

class X
{
	static void Simple ()
	{
		int a = 5;
		long b = 7;
		float f = (float) a / (float) b;

		string hello = "Hello World";

		Console.WriteLine (a);
		Console.WriteLine (b);
		Console.WriteLine (f);
		Console.WriteLine (hello);
	}

	static void BoxedValueType ()
	{
		int a = 5;
		object boxed_a = a;

		Console.WriteLine (a);
		Console.WriteLine (boxed_a);
	}

	static void BoxedReferenceType ()
	{
		string hello = "Hello World";
		object boxed_hello = hello;

		Console.WriteLine (hello);
		Console.WriteLine (boxed_hello);
	}

	static void SimpleArray ()
	{
		int[] a = { 3, 4, 5 };

		Console.WriteLine (a [2]);
	}

	static void MultiValueTypeArray ()
	{
		int[,] a = { { 6, 7, 8 }, { 9, 10, 11 } };

		Console.WriteLine (a [1,2]);
	}

	static void StringArray ()
	{
		string[] a = { "Hello", "World" };

		Console.WriteLine (a);
	}

	static void MultiStringArray ()
	{
		string[,] a = { { "Hello", "World" }, { "New York", "Boston" },
				{ "Ximian", "Monkeys" } };

		Console.WriteLine (a);
	}

	static void StructType ()
	{
		A a = new A (5, 256, "New England Patriots");
		Console.WriteLine (a);
	}

	static void ClassType ()
	{
		B b = new B (5, 256, "New England Patriots");
		Console.WriteLine (b);
	}

	static void InheritedClassType ()
	{
		C c = new C (5, 256, "New England Patriots", 3.14F, 8);
		Console.WriteLine (c.a);

		B b = c;
		Console.WriteLine (b.a);
	}

	static void ComplexStructType ()
	{
		A a = new A (5, 256, "New England Patriots");
		B b = new B (5, 256, "New England Patriots");
		C c = new C (5, 256, "New England Patriots", 3.14F, 8);

		D d = new D (a, b, c, "Eintracht Trier");
		Console.WriteLine (d.s [0]);
	}

	static void Main ()
	{
		Simple ();
		BoxedValueType ();
		BoxedReferenceType ();
		SimpleArray ();
		MultiValueTypeArray ();
		StringArray ();
		MultiStringArray ();
		StructType ();
		ClassType ();
		InheritedClassType ();
		ComplexStructType ();
	}
}
