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

		[Test]
		[Category("Generics")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main()");

			AssertExecute ("next");
			AssertStopped (thread, "main1", "X.Main()");

			AssertPrint (thread, "foo", "(Foo`1<int>) { Data = 5 }");
			AssertPrint (thread, "$parent (foo)", "(System.Object) { }");
			AssertType (thread, "foo",
				    "class Foo`1<int> = Foo`1<T> : System.Object\n" +
				    "{\n   T Data;\n   void Hello ();\n   .ctor (T);\n}");

			AssertExecute ("step");
			AssertStopped (thread, "foo hello", "Foo<T>.Hello()");

			AssertPrint (thread, "this", "(Foo`1<int>) { Data = 5 }");
			AssertType (thread, "this",
				    "class Foo`1<int> = Foo`1<T> : System.Object\n" +
				    "{\n   T Data;\n   void Hello ();\n   .ctor (T);\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertHitBreakpoint (thread, "main2", "X.Main()");

			AssertPrint (thread, "bar", "(Bar`1<int>) { <Foo`1<int>> = { Data = 5 } }");
			AssertPrint (thread, "$parent (bar)", "(Foo`1<int>) { Data = 5 }");
			AssertType (thread, "bar",
				    "class Bar`1<int> = Bar`1<U> : Foo`1<U>\n{\n   .ctor (U);\n}");
			AssertType (thread, "$parent (bar)",
				    "class Foo`1<int> = Foo`1<T> : System.Object\n" +
				    "{\n   T Data;\n   void Hello ();\n   .ctor (T);\n}");

			AssertExecute ("step");

			AssertStopped (thread, "foo hello", "Foo<T>.Hello()");

			AssertPrint (thread, "this", "(Bar`1<int>) { <Foo`1<int>> = { Data = 5 } }");
			AssertType (thread, "this",
				    "class Bar`1<int> = Bar`1<U> : Foo`1<U>\n{\n   .ctor (U);\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertHitBreakpoint (thread, "main3", "X.Main()");

			AssertPrintRegex (thread, DisplayFormat.Object, "baz",
					  @"\(Baz`1<int>\) { <Foo`1<Hello`1<int>>> = { Data = \(Hello`1<int>\) 0x[0-9a-f]+ } }");
			AssertPrintRegex (thread, DisplayFormat.Object, "$parent (baz)",
					  @"\(Foo`1<Hello`1<int>>\) { Data = \(Hello`1<int>\) 0x[0-9a-f]+ }");
			AssertPrint (thread, "$parent+1 (baz)", "(System.Object) { }");

			AssertType (thread, "baz",
				    "class Baz`1<int> = Baz`1<U> : Foo`1<Hello`1<U>>\n{\n   .ctor (U);\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("8");
			AssertTargetOutput ("Hello`1[System.Int32]");
			AssertHitBreakpoint (thread, "main4", "X.Main()");

			AssertPrint (thread, "test", "(Test) { <Foo`1<int>> = { Data = 9 } }");
			AssertPrint (thread, "$parent (test)", "(Foo`1<int>) { Data = 9 }");
			AssertType (thread, "test",
				    "class Test : Foo`1<int>\n{\n   static void Hello`1 (T);\n" +
				    "   .ctor ();\n}");
			AssertType (thread, "$parent (test)",
				    "class Foo`1<int> = Foo`1<T> : System.Object\n" +
				    "{\n   T Data;\n   void Hello ();\n   .ctor (T);\n}");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertTargetOutput ("8");
			AssertTargetOutput ("World");
			AssertTargetExited (thread.Process);
		}
	}
}
