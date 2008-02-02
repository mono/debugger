using System;
using System.Collections.Generic;

delegate int Foo ();

class X
{
	public static void Test1<R> (R r, int a)
	{
		for (int b = a; b > 0; b--) {
			R s = r;
			Console.WriteLine (s);			// @MDB LINE: test1
			Foo foo = delegate {
				Console.WriteLine (b);		// @MDB LINE: test1 foo
				Console.WriteLine (s);
				Console.WriteLine (a);
				Console.WriteLine (r);
				return 3;
			};
			a -= foo ();				// @MDB LINE: test2
		}
	}

	static void Main ()
	{
		Test1 (500L, 2);				// @MDB LINE: main
	}
}
