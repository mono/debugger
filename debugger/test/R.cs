using System;
using System.Collections;

class X
{
	int total;

	IEnumerator GetRange (int a)
	{
		yield return total * a;
	}

	void Test ()
	{
		IEnumerator e = GetRange (9);
		while (e.MoveNext ())
			total += (int) e.Current;

		Console.WriteLine (total);
	}

	static void Main ()
	{
		X x = new X ();
		x.Test ();
	}
}
