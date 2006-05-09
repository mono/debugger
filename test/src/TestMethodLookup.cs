using System;

public class Root
{
	public Root ()
	{ }

	public Root (int a)
	{ }

	public Root (Root root)
	{ }

	public Root (Foo.Bar.Test test)
	{ }

	public bool Property {
		get { return true; }
	}

	public static bool StaticProperty {
		get { return true; }
	}

	public bool Hello ()
	{
		return true;
	}

	public bool Simple (Foo.Bar.Test test)
	{
		return true;
	}

	public static bool Overloaded (string message)
	{
		return true;
	}

	public bool Overloaded ()
	{
		return true;
	}

	public bool Overloaded (int a)
	{
		return true;
	}

	public bool Overloaded (Root root)
	{
		return true;
	}

	public bool Overloaded (Foo.Bar.Test test)
	{
		return true;
	}

	public static bool StaticHello ()
	{
		return true;
	}

	public static bool StaticOverloaded ()
	{
		return true;
	}

	public static bool StaticOverloaded (int a)
	{
		return true;
	}

	public static bool StaticOverloaded (Root root)
	{
		return true;
	}

	public static bool StaticOverloaded (Foo.Bar.Test test)
	{
		return true;
	}
}

namespace Foo
{
	namespace Bar
	{
		public class Test
		{
			public Test ()
			{ }

			public Test (int a)
			{ }

			public Test (Root root)
			{ }

			public Test (Foo.Bar.Test test)
			{ }

			public bool Property {
				get { return true; }
			}

			public static bool StaticProperty {
				get { return true; }
			}

			public bool Hello ()
			{
				return true;
			}

			public bool Simple (Foo.Bar.Test test)
			{
				return true;
			}

			public static bool Overloaded (string message)
			{
				return true;
			}

			public bool Overloaded ()
			{
				return true;
			}

			public bool Overloaded (int a)
			{
				return true;
			}

			public bool Overloaded (Root root)
			{
				return true;
			}

			public bool Overloaded (Foo.Bar.Test test)
			{
				return true;
			}

			public static bool StaticHello ()
			{
				return true;
			}

			public static bool StaticOverloaded ()
			{
				return true;
			}

			public static bool StaticOverloaded (int a)
			{
				return true;
			}

			public static bool StaticOverloaded (Root root)
			{
				return true;
			}

			public static bool StaticOverloaded (Foo.Bar.Test test)
			{
				return true;
			}
		}
	}
}

public class X
{
	public X ()
	{ }

	public bool Property {
		get { return true; }
	}

	public static bool StaticProperty {
		get { return true; }
	}

	public bool Hello ()
	{
		return true;
	}

	public bool Simple (Foo.Bar.Test test)
	{
		return true;
	}

	public static bool Overloaded (string message)
	{
		return true;
	}

	public bool Overloaded ()
	{
		return true;
	}

	public bool Overloaded (int a)
	{
		return true;
	}

	public bool Overloaded (Root root)
	{
		return true;
	}

	public bool Overloaded (Foo.Bar.Test test)
	{
		return true;
	}

	public static bool StaticHello ()
	{
		return true;
	}

	public static bool StaticOverloaded ()
	{
		return true;
	}

	public static bool StaticOverloaded (int a)
	{
		return true;
	}

	public static bool StaticOverloaded (Root root)
	{
		return true;
	}

	public static bool StaticOverloaded (Foo.Bar.Test test)
	{
		return true;
	}

	public static void Main ()
	{
		Root root = new Root ();
		Foo.Bar.Test test = new Foo.Bar.Test ();

		Console.WriteLine (root);
		Console.WriteLine (test);
	}
}
