using System;
using System.Runtime.Remoting;
using System.Reflection;

public class Hello : MarshalByRefObject, IHello
{
	public void World ()
	{
		Console.WriteLine ("Hello World from {0}!", AppDomain.CurrentDomain);
	}
}

class X
{
	static int Main ()
	{
		AppDomain domain = AppDomain.CreateDomain ("Test");
		Assembly ass = Assembly.GetExecutingAssembly ();
		Hello hello = (Hello) domain.CreateInstanceAndUnwrap (ass.FullName, "Hello");
		Console.WriteLine ("TEST: {0}", hello);
		hello.World ();

		Hello bar = new Hello ();
		bar.World ();

		hello.World ();
		bar.World ();

		AppDomain.Unload (domain);
		return 0;
	}
}
