using System;

public class Foo : MarshalByRefObject
{
	public Foo ()
	{
		Console.Error.WriteLine ("FOO CTOR");
	}

	public Bar Test ()
	{
		Console.Error.WriteLine ("FOO TEST");
		return new Bar ();
	}
}

public class Bar : MarshalByRefObject
{
}
