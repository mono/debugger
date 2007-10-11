using System;
using System.Collections.Generic;
using C5;

class X
{
	static void Main ()
	{
		TreeBag<int> bag = new TreeBag<int> ();
		bag.Add (3);
		Console.WriteLine (bag);
	}
}
