using System;
using System.Collections.Generic;

public static class RunTests
{
	public static void Main ()
	{
		Test1.X test1 = new Test1.X ();				// @MDB LINE: main
		test1.Run ();

		Test2.X test2 = new Test2.X ();
		test2.Run ();
	}
}

namespace Test1
{
	public class X
	{
		int total;

		static IEnumerator<int> GetRange ()
		{
			yield return 1;					// @MDB LINE: test1 yield1

			{
				int a = 3;				// @MDB LINE: test1 lexical
				yield return a;				// @MDB LINE: test1 yield2
			}

			yield return 4;					// @MDB LINE: test1 yield3
		}

		public int Run ()
		{
			IEnumerator<int> e = GetRange ();		// @MDB BREAKPOINT: test1 run
			while (e.MoveNext ())				// @MDB LINE: test1 loop
				total += e.Current;			// @MDB LINE: test1 statement

			return total;					// @MDB LINE: test1 return
		}
	}
}

namespace Test2
{
	public class X
	{
		int total;
		bool stop;

		IEnumerator<int> GetRange ()
		{							// @MDB LINE: test2 iterator start
			while (total < 100) {				// @MDB LINE: test2 iterator loop
				if (stop)				// @MDB LINE: test2 iterator if
					yield break;			// @MDB LINE: test2 iterator break

				yield return total;			// @MDB LINE: test2 iterator yield
			}
		}

		public int Run ()
		{
			IEnumerator<int> e = GetRange ();		// @MDB BREAKPOINT: test2 run
			while (e.MoveNext ())				// @MDB LINE: test2 loop
				stop = true;				// @MDB LINE: test2 statement

			return total;					// @MDB LINE: test2 return
		}
	}
}
