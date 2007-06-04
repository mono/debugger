using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestBreakpoint : TestSuite
	{
		public TestBreakpoint ()
			: base ("TestBreakpoint")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_world = 19;
			const int line_test = 24;
			const int line_test_2 = 25;
			const int line_test_3 = 27;
			const int line_porta_nigra = 74;
			const int line_roman_baths = 86;
			const int line_main = 97;
			const int line_main_2 = 99;
			const int line_y_test = 115;

			const string porta_nigra_url = "http://de.wikipedia.org/wiki/Bild:Porta_Nigra_Trier.jpg";
			const string city_center_url = "http://de.wikipedia.org/wiki/Bild:Trier_Innenstadt.jpg";
			const string roman_baths_url = "http://de.wikipedia.org/wiki/Bild:Trier_roman_baths_DSC02378.jpg";

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_1 = AssertBreakpoint ("Martin.Baulig.Hello.Test");
			int bpt_2 = AssertBreakpoint (line_test_2);
			int bpt_3 = AssertBreakpoint ("TestBreakpoint.cs:" + line_test_3);
			int bpt_world = AssertBreakpoint ("Martin.Baulig.Hello.World");
			int bpt_main_2 = AssertBreakpoint (line_main_2);

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_1, "Martin.Baulig.Hello.Test()",
					     line_test);

			int bpt_porta_nigra = AssertBreakpoint ("-get trier.PortaNigra");
			// We are stopped on a breakpoint and use "continue" to step over it
			AssertExecute ("continue");

			AssertHitBreakpoint (thread, bpt_porta_nigra,
					     "Europe.Germany.Trier.get_PortaNigra()",
					     line_porta_nigra);

			AssertExecute ("next");
			// Step over the breakpoint with "next"
			AssertStopped (thread, "Europe.Germany.Trier.get_PortaNigra()",
				       line_porta_nigra + 1);

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_world,
					     "Martin.Baulig.Hello.World(Postcard.Picture)",
					     line_world);

			AssertExecute ("disable " + bpt_world);
			AssertExecute ("continue");
			AssertTargetOutput (porta_nigra_url);

			AssertHitBreakpoint (thread, bpt_2,
					     "Martin.Baulig.Hello.Test()", line_test_2);
			// We are stopped on a breakpoint and use "next" to step over it
			AssertExecute ("next");
			AssertTargetOutput (city_center_url);
			AssertStopped (thread, "Martin.Baulig.Hello.Test()", line_test_3 - 1);
			// If you remove the `Console.WriteLine ("Irish Pub")', the "next"
			// operation will complete _at_ the instruction where the next
			// breakpoint is, but it won't actually hit it.
			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertHitBreakpoint (thread, bpt_3, "Martin.Baulig.Hello.Test()",
					     line_test_3);

			// We are stopped on a breakpoint and use "step" to step over it
			AssertExecute ("step");
			AssertStopped (thread, "Europe.Germany.Trier.get_RomanBaths()",
				       line_roman_baths);

			AssertExecute ("continue");
			AssertTargetOutput (roman_baths_url);

			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			// We still have a breakpoint in Europe.Germany.Trier.PortaNigra's
			// property getter while we're runtime-invoke'ing it.
			AssertPrint (thread, "hello.Trier.PortaNigra.URL",
				     "(System.String) \"" + porta_nigra_url + "\"");

			int bpt_y_hello = AssertBreakpoint ("Y.Hello");
			AssertExecute ("disable " + bpt_y_hello);

			AssertExecute ("next");
			AssertTargetOutput ("Hello World");
			AssertStopped (thread, "X.Main()", line_main_2 + 1);

			AssertExecute ("disable " + bpt_1);
			AssertExecute ("disable " + bpt_2);
			AssertExecute ("disable " + bpt_3);
			AssertExecute ("disable " + bpt_porta_nigra);
			AssertExecute ("next");
			AssertTargetOutput (porta_nigra_url);
			AssertTargetOutput (city_center_url);
			AssertTargetOutput ("Irish Pub");
			AssertTargetOutput (roman_baths_url);

			AssertStopped (thread, "X.Main()", line_main_2 + 2);

			AssertExecute ("step");
			AssertStopped (thread, "Y.Test(Martin.Baulig.Hello)", line_y_test);
			AssertExecute ("finish");
			AssertTargetOutput (porta_nigra_url);
			AssertTargetOutput (city_center_url);
			AssertTargetOutput ("Irish Pub");
			AssertTargetOutput (roman_baths_url);
			AssertStopped (thread, "X.Main()", line_main_2 + 3);

			AssertExecute ("continue");
			AssertTargetOutput ("Martin.Baulig.Hello");
			AssertTargetExited (thread.Process);
		}
	}
}
