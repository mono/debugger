using System;

public class X
{
	public readonly long Foo;

	public X (long foo)
	{
		this.Foo = foo;
	}

	public static void Main ()
	{
		string hello = null;
		X x = null;
		int[] int_array = null;
		X[] x_array = null;
		X[] y_array = new X [1];
		X[] z_array = new X [] { new X (5) };

		Console.WriteLine (hello == null);
		Console.WriteLine (x == null);
		Console.WriteLine (int_array == null);
		Console.WriteLine (x_array == null);
		Console.WriteLine (y_array == null);
		Console.WriteLine (z_array == null);
	}
}
