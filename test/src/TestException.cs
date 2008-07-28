using System;

public class MyException : Exception
{ }

public class X
{
	public virtual void Test ()
	{
		throw new InvalidOperationException ();		// @MDB LINE: exception
	}

	public virtual void ThrowMy ()
	{
		throw new MyException ();			// @MDB LINE: throw my
	}

	void TestMy ()
	{
		try {
			ThrowMy ();				// @MDB BREAKPOINT: try my
		} catch (Exception ex) {
			Console.WriteLine ("MY EXCEPTION: {0}", ex.GetType ());
		}
	}

	static void Main ()
	{
		X x = new X ();					// @MDB LINE: main
		try {
			x.Test ();				// @MDB LINE: try
		} catch (InvalidOperationException ex) {
			Console.WriteLine ("EXCEPTION: {0}", ex.GetType ());
		}

		Console.WriteLine ("Done");			// @MDB BREAKPOINT: main2

		x.TestMy ();
	}
}
