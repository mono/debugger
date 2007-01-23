using System;
using System.Threading;

class X
{
	public static bool Silent = false;

	public X (string name)
	{
		this.Name = name;
	}

	public readonly string Name;
	public int Counter;

	static void Loop (int seconds)
	{
		Thread.Sleep (seconds * 50);
	}

	void LoopDone ()
	{
		if (!Silent)
			Console.WriteLine ("Loop: {0} {1}", Name, ++Counter);
	}

	void CommonFunction (int seconds)
	{
		for (;;) {
			Loop (seconds);
			LoopDone ();
		}
	}

	static void ThreadMain ()
	{
		X x = new X ("child");
		x.CommonFunction (7);
	}

	static void Main ()
	{
		Thread thread = new Thread (new ThreadStart (ThreadMain));
		thread.Start ();
		X x = new X ("main");
		x.CommonFunction (11);
	}
}
