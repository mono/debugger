using System;

class Whatever
{
	struct Foo
	{
		public int a;
		public long b;
		public float c;

		public Foo (int a, long b)
		{
			this.a = a;
			this.b = b;
			this.c = ((float) a) / ((float) b);
		}
	}

	class X {
		public int a = 5;
	}

	class Y : X {
		new public long a = 8;
	}
			
	static void Test (Foo foo)
	{
		Console.WriteLine (foo.c);
	}

	static void ClassTest (Y y)
	{
		Console.WriteLine (y.a);
	}

	static void ArrayTest (long[] b)
	{
		Console.WriteLine (b [1]);
	}

	static void MultiArrayTest (int[,] a)
	{
		Console.WriteLine (a [2,3]);
	}

	static void TimeTest (DateTime time)
	{
		Console.WriteLine (time);
	}

	static void ObjectTest (object o)
	{
		Console.WriteLine (o);
	}

	static void Main ()
	{
		ObjectTest (2);

		Foo foo = new Foo (5, 29);

		Test (foo);

		DateTime time = DateTime.Now;
		TimeTest (time);

		Y y = new Y ();
		ClassTest (y);

		long[] b = { 59, 8, -19 };
		ArrayTest (b);

		int[,] a = { {  5,  6,  7 }, {  8,  2,  4}, {  6,  1,  9 }, {  10,  50,  200 } };
		MultiArrayTest (a);
	}
}
