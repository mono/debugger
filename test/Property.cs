using System;

class X
{
	class Foo
	{
		public int Test {
			get {
				return 8;
			}
		}
	}

	static void Test (Foo foo)
	{
		int a = foo.Test;
		Console.WriteLine (a);
	}

	static void Main ()
	{
		Foo foo = new Foo ();

		Test (foo);
	}
}
