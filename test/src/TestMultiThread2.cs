using System;
using System.Threading;

class X
{
	static void ThreadMain ()
	{
		Console.WriteLine ("Thread main!");	// @MDB BREAKPOINT: thread main
	}

	static void Test ()
	{
		Thread thread = new Thread (ThreadMain);
		thread.Start ();
		Thread.Sleep (100000);
	}

	static void Main ()
	{
		Test ();				// @MDB LINE: main
	}
}
