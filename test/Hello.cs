using System;

public class Hello
{
	public static void Test ()
	{
		Hello hello = new Hello ();
		hello.World ();
	}

	public virtual void World ()
	{
		int a = Int32.Parse ("92");
	}
}
