using System;
using System.Diagnostics;

class X
{
	static void Main (string[] args)
	{
		Process process = Process.Start (args [0]);
		process.WaitForExit ();
	}
}
