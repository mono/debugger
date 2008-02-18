using System;
using System.Collections;

class X
{
	static int total;

	static IEnumerator GetRange ()
	{
		yield return 1;

		{
			int a = 3;
			yield return a;
		}

		for (;;) {
			if (total > 3)
				yield break;

			yield return 4;
		}
	}

	static void Main ()
	{
		IEnumerator e = GetRange ();
		while (e.MoveNext ())
			total += (int) e.Current;

		Console.WriteLine (total);
	}
}
