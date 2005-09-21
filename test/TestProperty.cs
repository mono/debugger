using System;

public class A
{ }

public class B
{ }

public class C
{ }

public class D
{ }

public class Test
{
	public A A {
		get { return new A (); }
	}

	public B[] B {
		get {
			B[] b = new B [1];
			b [0] = new B ();
			return b;
		}
	}

	public C[,] C {
		get { return new C [0,0]; }
	}

	public string Hello (D d)
	{
		return d.ToString ();
	}
}

class X
{
	static void Main ()
	{
		Test test = new Test ();
		Console.WriteLine (test);
	}
}
