using System;

public abstract class Foo
{
}

public class Bar : Foo
{
	public readonly int Data;

	public Bar (int data)
	{
		this.Data = data;
	}

	public void Hello ()
	{ }
}

public struct Hello
{
	public readonly long Data;

	public Hello (long data)
	{
		this.Data = data;
	}

	public override string ToString ()
	{
		return String.Format ("0x{0:x}", Data);
	}
}

class X
{
	static void Main ()
	{
		Foo foo = new Bar (81);
		Hello hello = new Hello (0x12345678);
		object obj = foo;
		object boxed = hello;
		ValueType value = hello;
		Console.WriteLine (foo);
		Console.WriteLine (obj);
		Console.WriteLine (boxed);
		Console.WriteLine (value);
	}
}
