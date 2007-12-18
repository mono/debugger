using System;

public class X
{
	string[] array;

	public X (params string[] args)
	{
		this.array = args;
	}

	public string this [int index] {
		get {
			return array [index];
		}

		set {
			array [index] = value;
		}
	}

	public string this [int index, string text] {
		get {
			return array [index] + " " + text;
		}
	}

	public static void Main ()
	{
		X x = new X ("Hello", "World");

		Console.WriteLine (x [0]);
	}
}
