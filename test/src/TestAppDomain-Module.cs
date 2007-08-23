using System;
using System.Runtime.Remoting;
using System.Reflection;

class X
{
	static void Main ()
	{
		AppDomain domain = AppDomain.CreateDomain ("Test");
		IHello hello = (IHello) domain.CreateInstanceAndUnwrap ("TestAppDomain-Hello", "Hello");

		hello.World ();

		AppDomain.Unload (domain);

		Console.WriteLine ("UNLOADED!");
	}
}
