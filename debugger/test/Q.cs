using System;

public delegate void Foo (long a);

class Test
{
	public static void Hello ()
	{
		Console.WriteLine ("Hello");

		{
			int a = 5;
			
			Foo foo = delegate (long r) {
				Console.WriteLine (r + a);
			};

			foo (9);
		}

		Console.WriteLine ("World");

		{
			int a = 8;
			
			Foo foo = delegate (long r) {
				Console.WriteLine (r + a);
			};

			foo (11);
		}

		Console.WriteLine ("Galaxy");
	}
}

class X
{
	static void Main ()
	{
		Test.Hello ();
	}
}
