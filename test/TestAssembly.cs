using System;
using System.Reflection;

class X
{
	static void Main (string[] args)
	{
		if (args.Length != 1)
			throw new Exception ();
		Assembly ass = Assembly.LoadFrom (args [0]);
		Type module = ass.GetType ("Module");
		MethodInfo method = module.GetMethod ("Test");
		method.Invoke (null, new object[0]);
	}
}
