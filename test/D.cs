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

	static void Main ()
	{
		Simple ();
		BoxedValueType ();
		BoxedReferenceType ();
		SimpleArray ();
		MultiValueTypeArray ();
		StringArray ();
		MultiStringArray ();
		ClassType ();
		StructType ();
	}
}
