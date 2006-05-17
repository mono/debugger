using System;
using System.Diagnostics;

class X
{
	static int Main (string[] args)
	{
		string[] new_args = new string [args.Length - 1];
		Array.Copy (args, 1, new_args, 0, args.Length - 1);
		Process process = Process.Start (args [0], String.Join (" ", new_args));
		process.WaitForExit ();
		return process.ExitCode;
	}
}
