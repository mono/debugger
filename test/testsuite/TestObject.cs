using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestObject : TestSuite
	{
		public TestObject ()
			: base ("../test/src/TestObject.exe")
		{ }

		[Test]
		[Category("NotWorking")]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 39;
			const int line_main_2 = 44;

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2.ToString ());
			Execute ("continue");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			AssertPrint (thread, "obj", "(object) &(Bar) { Data = 81 }");
			AssertPrint (thread, "boxed", "(object) &(Hello) { Data = 305419896 }");
			AssertPrint (thread, DisplayFormat.HexaDecimal, "boxed",
				     "(object) &(Hello) { Data = 0x12345678 }");
			AssertPrint (thread, "obj.ToString ()", "(System.String) \"Bar\"");
			AssertPrint (thread, "obj.GetType ()", "(System.MonoType) { \"Bar\" }");
			AssertPrint (thread, "boxed.GetType()", "(System.MonoType) { \"Hello\" }");
			AssertPrint (thread, "boxed.ToString ()", "(System.String) \"0x12345678\"");
			AssertPrint (thread, "value", "(System.ValueType) { }");
			AssertPrint (thread, "value.GetType ()", "(System.MonoType) { \"Hello\" }");
			AssertPrint (thread, "value.ToString ()", "(System.String) \"0x12345678\"");

			Execute ("kill");
		}
	}
}
