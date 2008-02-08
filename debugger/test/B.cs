using System;

namespace Test
{
	public class Foo<T>
	{
		public static void Hello<S> (S s, T t)
		{
			Console.WriteLine (s);
			Console.WriteLine (t);
		}
	}
}

class X
{
	static void Main ()
	{
		Test.Foo<int>.Hello ("Hello World", 8);
	}
}
