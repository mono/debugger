using System;

struct A
{
	public int Foo;
}

struct B
{
	public A A;
}

struct C
{
	public B B;
	public object Object;
}

class X
{
	static void Main ()
	{
		C c = new C ();
		c.B.A.Foo = 31;
		c.Object = c.B;
		Console.WriteLine (c.B.A.Foo);
	}
}
