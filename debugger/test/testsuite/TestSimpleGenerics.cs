using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestSimpleGenerics : TestSuite
	{
		public TestSimpleGenerics ()
			: base ("TestSimpleGenerics")
		{ }

		const int LineFooHello = 22;

		const int LineMain = 56;
		const int LineMain2 = 60;
		const int LineMain3 = 63;
		const int LineMain4 = 67;

		int bpt_main_2;
		int bpt_main_3;
		int bpt_main_4;

		[Test]
		[Category("Generics")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", LineMain);
			bpt_main_2 = AssertBreakpoint (LineMain2);
			bpt_main_3 = AssertBreakpoint (LineMain3);
			bpt_main_4 = AssertBreakpoint (LineMain4);

			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", LineMain + 1);

			AssertPrint (thread, "foo", "(Foo`1<int>) { Data = 5 }");
			AssertType (thread, "foo",
				    "class Foo`1<int> = Foo`1<T> : System.Object\n" +
				    "{\n   T Data;\n   void Hello ();\n   .ctor (T);\n}");

			AssertExecute ("step");
			// FIXME: Use correct method name
			AssertStopped (thread, "Foo`1.Hello()", LineFooHello);

			AssertPrint (thread, "this", "(Foo`1<int>) { Data = 5 }");
			AssertType (thread, "this",
				    "class Foo`1<int> = Foo`1<T> : System.Object\n" +
				    "{\n   T Data;\n   void Hello ();\n   .ctor (T);\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", LineMain2);

			AssertPrint (thread, "bar", "(Bar`1<int>) { <Foo`1<int>> = { Data = 5 } }");
			AssertType (thread, "bar",
				    "class Bar`1<int> = Bar`1<U> : Foo`1<U>\n{\n   .ctor (U);\n}");

			AssertExecute ("step");

			AssertStopped (thread, "Foo`1.Hello()", LineFooHello);

			AssertPrint (thread, "this", "(Bar`1<int>) { <Foo`1<int>> = { Data = 5 } }");
			AssertType (thread, "this",
				    "class Bar`1<int> = Bar`1<U> : Foo`1<U>\n{\n   .ctor (U);\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertHitBreakpoint (thread, bpt_main_3, "X.Main()", LineMain3);

			AssertPrintRegex (thread, DisplayFormat.Object, "baz",
					  @"\(Baz`1<int>\) { <Foo`1<Hello`1<int>>> = { Data = \(Hello`1<int>\) 0x[0-9a-f]+ } }");
			AssertType (thread, "baz",
				    "class Baz`1<int> = Baz`1<U> : Foo`1<Hello`1<U>>\n{\n   .ctor (U);\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("8");
			AssertTargetOutput ("Hello`1[System.Int32]");
			AssertHitBreakpoint (thread, bpt_main_4, "X.Main()", LineMain4);

			AssertPrint (thread, "test", "(Test) { <Foo`1<int>> = { Data = 9 } }");
			AssertType (thread, "test",
				    "class Test : Foo`1<int>\n{\n   static void Hello`1 (T);\n" +
				    "   .ctor ();\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertTargetOutput ("8");
			AssertTargetOutput ("World");
			AssertTargetExited (thread.Process);
		}
	}
}
