using System;
using System.Runtime.Remoting;
using System.Reflection;

class X
{
	static void Main ()
	{
		AppDomain domain = AppDomain.CreateDomain ("Test");	// @MDB LINE: main
		IHello hello = (IHello) domain.CreateInstanceAndUnwrap ("TestAppDomain-Hello", "Hello");

		hello.World ();						// @MDB LINE: main2

		AppDomain.Unload (domain);				// @MDB BREAKPOINT: unload

		Console.WriteLine ("UNLOADED!");			// @MDB LINE: end
	}
}
