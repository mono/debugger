using System;

class X
{
	public void Foo ()
	{ }

	static void Test (X x)
	{
		Console.WriteLine ("Start!");			// @MDB BREAKPOINT: test

#line hidden
		Console.WriteLine ("Hidden!");
		x.Foo ();					// @MDB LINE: test foo
		Console.WriteLine ("Last hidden line!");
#line default

		Console.WriteLine ("End!");			// @MDB LINE: test end
	}

	static void Main ()
	{
		Test (new X ());				// @MDB LINE: main
		Test (null);
	}
}
