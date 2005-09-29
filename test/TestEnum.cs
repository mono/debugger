using System;

public enum Music
{
	Irish,
	Country,
	RockPop,
	HeavyMetal,
	Techno,
	HipHop	
}

[Flags]
public enum Drinks : long
{
	NonAlcoholic	= Water | Juice | Coffee | Tea,

	Water		= 0x1001,
	Juice		= 0x1002,
	Coffee		= 0x1004,
	Tea		= 0x1008,

	Alcoholic	= Beer | Whine | Vodka | Rum | Tequila,

	Beer		= 0x2001,
	Whine		= 0x2002,
	Vodka		= 0x2004,
	Rum		= 0x2008,
	Tequila		= 0x2010,

	All		= NonAlcoholic | Alcoholic
}

public class Pub
{
	public readonly Music Music;
	public readonly Drinks Drinks;

	public Pub (Music music, Drinks drinks)
	{
		this.Music = music;
		this.Drinks = drinks;
	}
}

class X
{
	static void Main ()
	{
		Pub irish_pub_thursday = new Pub (Music.Irish, Drinks.All);
		Pub lunch_break = new Pub (Music.RockPop, Drinks.Coffee | Drinks.Water);
		Pub dinner = new Pub (Music.Country, Drinks.Tea | Drinks.Juice);

		Console.WriteLine (irish_pub_thursday);
		Console.WriteLine (lunch_break);
		Console.WriteLine (dinner);
	}
}
