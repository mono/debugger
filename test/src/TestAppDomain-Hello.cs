using System;

public class Hello : MarshalByRefObject, IHello
{
	public void World ()
	{
		Console.WriteLine ("Hello World from {0}!", AppDomain.CurrentDomain);
	}
}
