using System;
using System.Threading;

class X
{
	static void ThreadMain ()
	{
		while (true) {
			Console.WriteLine ("CHILD THREAD!");
			Thread.Sleep (1000);
		}
	}

	static void Main ()
	{
		Console.WriteLine ("STARTING!");

		Thread thread = new Thread (new ThreadStart (ThreadMain));
		thread.Start ();

		while (true) {
			Console.WriteLine ("PARENT THREAD!");
			Thread.Sleep (500);
		}
	}
}
