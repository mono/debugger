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
		return String.Format ("Foo ({0})", Time);
	}

	public Foo Sleep ()
	{
		ST.Thread.Sleep (Time * 100);
		return this;
	}
}

class X
{
	static void Main ()
	{
		Foo a = new Foo (5);				// @MDB LINE: main
		a.Sleep ();
	}
}
