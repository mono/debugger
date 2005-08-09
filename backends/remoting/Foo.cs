using System;

public class Foo : MarshalByRefObject
{
	public Foo ()
	{
		Console.Error.WriteLine ("FOO CTOR");
	}

	public void Hello ()
	{
		Console.WriteLine ("HELLO");
		System.Threading.Thread.Sleep (10000);
		Console.WriteLine ("WORLD");
	}

	public Bar Test ()
	{
		Console.Error.WriteLine ("FOO TEST");
		return new Bar ();
	}
}

public class Bar : MarshalByRefObject
{
	public void Hello ()
	{
		Console.WriteLine ("BOSTON!");
	}
}
