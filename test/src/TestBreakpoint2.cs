using System;

class X
{
	static void Main ()
	{
		Run ();				// @MDB LINE: main
	}

	static void Run ()
	{
		Foo foo = new Foo ();		// @MDB LINE: run
		foo.Run ();
	}
}
