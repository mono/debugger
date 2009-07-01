using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestRecursiveGenerics : DebuggerTestFixture
	{
		public TestRecursiveGenerics ()
			: base ("TestRecursiveGenerics")
		{ }

		[Test]
		[Category("Generics")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", GetLine ("main"));

			AssertType (thread, "test",
				    "class Test : Foo`1<Test>\n{\npublic:\n   .ctor ();\n}");

			AssertExecute ("next");
			AssertStopped (thread, "X.Main()", GetLine ("main") + 1);

			AssertPrintRegex (thread, DisplayFormat.Object, "test",
					  @"\(Test\) { <Foo`1<Test>> = { Data = \(Test\) 0x[0-9a-f]+ } }");

			AssertExecute ("continue");
			AssertTargetOutput ("Test");
			AssertTargetExited (thread.Process);
		}
	}
}
