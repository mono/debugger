using System;
using System.Threading;

class X
{
	static void CommonFunction (bool is_thread, int sleep)
	{
		do {
			Console.WriteLine ("COMMON FUNCTION: {0}", is_thread);
			Thread.Sleep (sleep);
		} while (true);
	}

	static void ThreadMain ()
	{
		CommonFunction (true, 5000);
	}

	static void Main ()
	{
		Console.WriteLine ("STARTING!");

		Thread thread = new Thread (new ThreadStart (ThreadMain));
		thread.Start ();

		CommonFunction (false, 1000);
	}
}
