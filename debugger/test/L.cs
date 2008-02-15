using System;
using System.Collections.Generic;

delegate int Foo ();

class X
{
	static void Main ()
	{
		Test (8);
	}

	public static void Test (int a)
	{
		for (int b = a; b > 0; b--) {
			int s = a;
			Foo foo = delegate {
				Console.WriteLine (a);
				Console.WriteLine (b);
				Console.WriteLine (s);
				return 3;
			};
			a -= foo ();
		}
	}
}
