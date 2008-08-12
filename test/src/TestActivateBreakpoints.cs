using System;
using System.Threading;

class X
{
	static void Main (params string[] args)
	{
		bool loop = args.Length == 0;		// @MDB LINE: main
		Thread.Sleep (100);
		Console.WriteLine (loop);		// @MDB LINE: main2

		long count = 0;
		while (loop) {
			count++;			// @MDB LINE: loop
		}

		Console.WriteLine ("Stop");		// @MDB BREAKPOINT: stop

		loop = args.Length == 0;
		while (loop) {
			Thread.Sleep (100);		// @MDB LINE: second loop
		}

		Console.WriteLine ("Done");
	}
}
