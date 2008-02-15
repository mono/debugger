using System;
using System.Collections;

class X
{
	static IEnumerator GetRange ()
	{
		yield return 1;
		yield return 2;

		{
			int a = 3;
			yield return a;
		}

		yield return 4;
		yield return 5;
	}

	static void Main ()
	{
		int total = 0;
		
		IEnumerator e = GetRange ();
		while (e.MoveNext ())
			total += (int) e.Current;

		Console.WriteLine (total);
	}
}
