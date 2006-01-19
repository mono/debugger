using System;

namespace Martin.Baulig
{
	using Europe.Germany;
	using Postcard;

	public class Hello
	{
		protected Trier trier;

		public Hello ()
		{
			trier = new Trier ();
		}

		public static void World (Picture picture)
		{
			Console.WriteLine (picture);
		}

		public void Test ()
		{
			World (trier.PortaNigra);
			World (trier.CityCenter);
			Console.WriteLine ("Irish Pub");
			World (Trier.RomanBaths);
		}

		public Trier Trier {
			get {
				return trier;
			}
		}
	}
}

namespace Postcard
{
	public abstract class Picture
	{ }
}

namespace Europe
{
	using Postcard;

	public class BeautifulPicture : Picture
	{
		public readonly string URL;

		public BeautifulPicture (string url)
		{
			this.URL = url;
		}

		public override string ToString ()
		{
			return URL;
		}
	}

	namespace Germany
	{
		public class Trier
		{
			const string WikipediaRoot = "http://de.wikipedia.org/wiki/";
			const string PortaNigraURL = "Bild:Porta_Nigra_Trier.jpg";
			const string CityCenterURL = "Bild:Trier_Innenstadt.jpg";
			const string RomanBathsURL = "Bild:Trier_roman_baths_DSC02378.jpg";

			public Picture PortaNigra {
				get {
					return new BeautifulPicture (WikipediaRoot + PortaNigraURL);
				}
			}

			public Picture CityCenter {
				get {
					return new BeautifulPicture (WikipediaRoot + CityCenterURL);
				}
			}

			public static Picture RomanBaths {
				get {
					return new BeautifulPicture (WikipediaRoot + RomanBathsURL);
				}
			}
		}
	}
}

class X
{
	static void Main ()
	{
		Martin.Baulig.Hello hello = new Martin.Baulig.Hello ();
		hello.Test ();
		Y.Hello ();
		hello.Test ();
		Console.WriteLine (hello);
	}
}

public class Y
{
	public static void Hello ()
	{
		Console.WriteLine ("Hello World");
	}
}
