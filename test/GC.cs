using System;
using System.Threading;

class X
{
	static void Loop ()
	{
		long a = 0;

		for (;;) {
			a *= 2;
		}
	}

	static void ThreadMain ()
	{
		Loop ();
	}

	static void Main ()
	{
		Thread thread = new Thread (new ThreadStart (ThreadMain));
		thread.Start ();

		GC.Collect ();
	}
}
