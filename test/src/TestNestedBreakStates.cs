using System;

public class MyException : Exception
{
	public MyException (string message)
		: base (message)
	{ }
}

public class X
{
	public double Test ()
	{
		return Math.PI;				// @MDB BREAKPOINT: test
	}

	public int Foo ()
	{
		return 9;
	}

	public void Exception ()
	{
		throw new MyException ("first");	// @MDB LINE: first
	}

	public void OtherException ()
	{
		throw new MyException ("second");	// @MDB LINE: second
	}

	public void HandledException ()
	{
		try {
			Exception ();			// @MDB LINE: exception
		} catch (MyException) {
			throw;				// @MDB LINE: rethrow
		}
	}

	static void Main ()
	{
		X x = new X ();
		x.Test ();				// @MDB BREAKPOINT: main
		x.HandledException ();
	}
}
