using System;
using System.Collections.Generic;

public static class RunTests
{
	public static void Main ()
	{
		Test1.X.Run ();						// @MDB LINE: main
		Test2.X.Run ();
	}
}

namespace Test1
{
	delegate int Foo ();

	public class X
	{
		public static void Test1<R> (R r, int a)
		{
			for (int b = a; b > 0; b--) {
				R s = r;
				Console.WriteLine (s);			// @MDB BREAKPOINT: test1
				Foo foo = delegate {
					Console.WriteLine (b);		// @MDB LINE: test1 foo
					Console.WriteLine (s);
					Console.WriteLine (a);
					Console.WriteLine (r);
					return 3;
				};
				a -= foo ();				// @MDB BREAKPOINT: test1 after foo
			}
		}

		public static void Run ()
		{
			Test1 (500L, 2);				// @MDB LINE: test1 run
		}
	}
}

namespace Test2
{
	delegate void Foo ();

	public class X
	{
		public void Hello<U> (U u)
		{ }							// @MDB BREAKPOINT: test2 hello

		public void Test<T> (T t)
		{
			T u = t;
			Hello (u);					// @MDB BREAKPOINT: test2
			Foo foo = delegate {
				Hello (u);				// @MDB LINE: test2 foo
			};
			foo ();						// @MDB BREAKPOINT: test2 after foo
			Hello (u);
		}

		public static void Run ()
		{
			X x = new X ();
			x.Test (3);
		}
	}
}
