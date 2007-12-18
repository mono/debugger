using System;
using System.Threading;

public class X
{
	public X (string name, int seconds)
	{
		this.Name = name;
		this.Seconds = seconds;
	}

	public readonly string Name;
	public readonly int Seconds;
	public static X Parent;
	public static X Child;
	public int Counter;

	void Loop ()
	{
		Thread.Sleep (Seconds * 150);
	}

	public int Test ()
	{
		Loop ();
		return Counter;
	}

	void LoopDone ()
	{
		Console.WriteLine ("Loop: {0} {1}", Name, Counter);
		Counter++;
	}

	void CommonFunction ()
	{
		for (;;) {
			Loop ();
			LoopDone ();
		}
	}

	static void ThreadMain ()
	{
		X x = Child = new X ("child", 7);
		x.CommonFunction ();
	}

	static void Main ()
	{
		Thread thread = new Thread (new ThreadStart (ThreadMain));
		thread.Start ();
		X x = Parent = new X ("main", 17);
		x.CommonFunction ();
	}
}
