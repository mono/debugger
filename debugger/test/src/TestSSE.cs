using System;

class RunTests
{
	static void Main ()
	{
		WhileLoop.Run ();				// @MDB LINE: main
		Foreach.Run ();
	}
}

public class WhileLoop
{
	static int total;

	static bool Test ()
	{
		return total < 50;				// @MDB LINE: while test
	}

	static object Total {
		get {
			return total;				// @MDB LINE: while total
		}
	}

	public static int Run ()
	{
		total = 4;					// @MDB BREAKPOINT: while run

		while (Test ())					// @MDB LINE: while loop
			total += (int) Total;			// @MDB LINE: while statement

		return total;					// @MDB BREAKPOINT: while return
	}
}

public class Foreach
{
	public static int[] Values {
		get { return new[] { 3, 9, 16, 25 }; }		// @MDB LINE: foreach values
	}

	public static int Run ()
	{
		int total = 0;					// @MDB BREAKPOINT: foreach run
		foreach (int value in Values)			// @MDB LINE: foreach loop
			total += value;				// @MDB LINE: foreach statement
		return total;					// @MDB BREAKPOINT: foreach return
	}
}
