using System;

class A
{
	public B Foo;
}

class B
{
	public int Test;
	public A Bar;
}

class X
{
	static void Test (A a, B b)
	{
		Console.WriteLine ("Hello World!");

		Console.WriteLine ("New York");
		Console.WriteLine ("Boston");
	}

	static void Main ()
	{
		A a = new A ();
		B b = new B ();

		b.Test = 8;
		a.Foo = b;
		b.Bar = a;

		Test (a, b);
	}
}
