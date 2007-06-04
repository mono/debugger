using System;
using System.Threading;

class X
{
	static void Loop (int seconds)
	{
		Thread.Sleep (seconds * 100);
	}

	static void CommonFunction (int seconds)
	{
		for (;;) {
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
		Thread thread = new Thread (new ThreadStart (ThreadMain));
		thread.Start ();

		CommonFunction (15);
	}
}
