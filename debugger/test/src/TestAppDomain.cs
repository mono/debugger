using System;
using System.Runtime.Remoting;
using System.Reflection;

public class Foo : MarshalByRefObject
{
	public void Hello ()
	{
		Console.WriteLine ("Hello World from {0}!", AppDomain.CurrentDomain);
	}
}

class X
{
	static void Main ()
	{
		AppDomain domain = AppDomain.CreateDomain ("Test");
		Assembly ass = Assembly.GetExecutingAssembly ();
		Foo foo = (Foo) domain.CreateInstanceAndUnwrap (ass.FullName, "Foo");
		Console.WriteLine ("TEST: {0}", foo);
		foo.Hello ();

		Foo bar = new Foo ();
		bar.Hello ();

		AppDomain.Unload (domain);
	}
}
