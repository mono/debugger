using System;

class X
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

	static void Main ()
	{
		Foo foo = new Foo (5, 29);

		Test (foo);

		Y y = new Y ();
		ClassTest (y);
	}
}
