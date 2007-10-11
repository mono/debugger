using System;

public class Test
{
	public static void Array<S> (S[] array)
	{
		foreach (S s in array)
			Console.WriteLine (s);
	}
}

class X
{
	static void Main ()
	{
		int[] a = { 3, 4, 5 };
		Test.Array (a);
	}
}
