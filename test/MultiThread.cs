using System;
using System.Threading;

class X
{
	static void Loop (int seconds)
	{
		Thread.Sleep (seconds * 1000);
	}

	static void CommonFunction (int seconds)
	{
		for (;;) {
			// When you reach this line from the main program, `Loop'
			// will block forever for you, but not for the other thread.
			//
			// So try to do a `NextLine' over the function call and the
			// other thread will hit your breakpoint.
			Console.WriteLine ("Loop: {0}", seconds);
			Loop (seconds);
		}
	}

	static void ThreadMain ()
	{
		CommonFunction (5);
	}

	static void Main ()
	{
		Console.WriteLine ("STARTING!");

		Thread thread = new Thread (new ThreadStart (ThreadMain));
		thread.Start ();

		// Ok, so here's how this works:
		// You start this application and when you reach this line, you
		// put the other thread into the background (with the `background')
		// command.
		//
		// Then, you step into `CommonFunction' and continue reading above.

		CommonFunction (15);
	}
}
