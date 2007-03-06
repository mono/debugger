using System;

public class X
{
	static void Main ()
	{
		Console.WriteLine ("Hello World");

		X x = new X ();
		x.Test ();
	}

	public void Test ()
	{
		Foo.Bar ();
	}
}

public static class Foo
{
	public static void Bar ()
	{
		Hello hello = new Hello ();
		Console.WriteLine ("Irish Pub");
		hello.World ();
	}
}

public class Hello
{
	public void World ()
	{
		Console.WriteLine ("WORLD!");
	}
}
