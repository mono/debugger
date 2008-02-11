using System;
using System.Collections.Generic;

public static class RunTests
{
	public static void Main ()
	{
		Test1.X.Run ();						// @MDB LINE: main
		Test2.X.Run ();
		Test3.X.Run ();
		Test4.X.Run ();
	}
}

// gtest-anon-8.cs
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

// gtest-anon-1.cs
namespace Test2
{
	delegate void Foo ();

	public class X
	{
		public U Hello<U> (U u)
		{
			return u;
		}							// @MDB BREAKPOINT: test2 hello

		public void Test<T> (T t)
		{
			T u = t;
			Hello (u);					// @MDB BREAKPOINT: test2
			Foo foo = delegate {
				Hello (u);				// @MDB LINE: test2 foo
				Hello (t);
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

// gtest-anon-3.cs
namespace Test3
{
	delegate void Foo<S> (S s);

	public class X
	{
		public U Hello<U> (U u)
		{
			return u;					// @MDB BREAKPOINT: test3 hello
		}

		public void Test<T> (T t)
		{
			Hello (t);					// @MDB BREAKPOINT: test3
			Foo<T> foo = delegate (T u) {
				Hello (u);				// @MDB LINE: test3 foo
				Hello (t);
			};
			foo (t);					// @MDB BREAKPOINT: test3 after foo
		}

		public static void Run ()
		{
			X x = new X ();
			x.Test (3);
		}
	}
}

// gtest-anon-15.cs
namespace Test4
{
	public delegate void Foo<V> (V v);

	public delegate void Bar<W> (W w);

	public class Test<T>
	{
		public static void Hello<S> (T t, S s)
		{
			Foo<long> foo = delegate (long r) {		// @MDB LINE: test4 foo
				Console.WriteLine (r);
				Bar<T> bar = delegate (T x) {
					Console.WriteLine (r);		// @MDB LINE: test4 bar
					Console.WriteLine (t);
					Console.WriteLine (s);
					Console.WriteLine (x);
				};
				bar (t);				// @MDB LINE: test4 foo2
			};
			foo (5);					// @MDB BREAKPOINT: test4
		}
	}

	public class X
	{
		public static void Run ()
		{
			Test<string>.Hello ("Kahalo", Math.PI);
		}
	}
}
