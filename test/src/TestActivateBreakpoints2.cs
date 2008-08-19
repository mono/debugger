using System;
using System.Threading;

class X
{
	long count;
	bool loop;
	Thread blocking, sleeping, executing;
	ManualResetEvent blocking_event = new ManualResetEvent (false);

	X (bool loop)
	{
		this.loop = loop;
	}

	void BlockingThread ()
	{
		blocking_event.WaitOne ();
		Console.WriteLine ("Blocking Done");
	}

	void SleepingThread ()
	{
		while (loop) {
			Thread.Sleep (100);
		}
	}

	void ExecutingThread ()
	{
		while (loop) {
			count++;		// @MDB LINE: executing
		}
		blocking_event.Set ();
	}

	void StartThreads ()
	{
		blocking = new Thread (new ThreadStart (BlockingThread));
		blocking.Start ();

		sleeping = new Thread (new ThreadStart (SleepingThread));	// @MDB BREAKPOINT: thread start1
		sleeping.Start ();

		executing = new Thread (new ThreadStart (ExecutingThread));	// @MDB BREAKPOINT: thread start2
		executing.Start ();

		Console.WriteLine (loop);
		executing.Join ();						// @MDB BREAKPOINT: thread start3
	}

	static void Main (params string[] args)
	{
		X x = new X (args.Length == 0);					// @MDB LINE: main
		x.StartThreads ();
	}
}
