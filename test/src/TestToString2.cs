using System;
using ST = System.Threading;

public class Foo
{
	public readonly int Time;

	public Foo (int time)
	{
		this.Time = time;
	}

	public override string ToString ()
	{
		ST.Thread.Sleep (Time * 100);
		return String.Format ("Foo ({0})", Time);
	}
}

class X
{
	static void Main ()
	{
		Foo a = new Foo (5);				// @MDB LINE: main
		Foo b = new Foo (13);

		Console.WriteLine ("TEST: {0} {1}", a, b);	// @MDB BREAKPOINT: main2
	}
}
