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

	static void Hello (Foo foo)
	{
		int a = foo.Test;
		Console.WriteLine (a);
	}

	static void Test (object test, object valuetype)
	{
	}

	static void Error (Exception e)
	{
	}

	static void Main ()
	{
		Foo foo = new Foo ();

		Hello (foo);
		Test ("Hello World", 5);
		Error (new InvalidOperationException ());
	}
}
