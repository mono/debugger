using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class testnativetypes : TestSuite
	{
		public testnativetypes ()
			: base ("testnativetypes", "testnativetypes.c")
		{ }

		const int LineMain = 140;
		const int LineSimple = 52;
		const int LineStruct = 71;
		const int LineStruct2 = 84;
		const int LineStruct3 = 95;
		const int LineFunctionStruct = 109;
		const int LineBitField = 123;
		const int LineList = 134;

		[Test]
		[Category("NativeTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "main", LineMain);

			int bpt_simple = AssertBreakpoint (LineSimple);
			int bpt_struct = AssertBreakpoint (LineStruct);
			int bpt_struct2 = AssertBreakpoint (LineStruct2);
			int bpt_struct3 = AssertBreakpoint (LineStruct3);
			int bpt_function_struct = AssertBreakpoint (LineFunctionStruct);
			int bpt_bitfield = AssertBreakpoint (LineBitField);
			int bpt_list = AssertBreakpoint (LineList);


			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_simple, "simple", LineSimple);

			AssertPrint (thread, "a", "(int) 5");
			AssertPrint (thread, "b", "(long int) 7");
			AssertPrint (thread, "f", "(float) 0.7142857");
			AssertPrint (thread, "hello", "(char *) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Simple: 5 - 7 - 0.714286 - Hello World");
			AssertHitBreakpoint (thread, bpt_struct, "test_struct", LineStruct);

			AssertPrint (thread, "s",
				     "(_TestStruct) { a = 5, b = 7, f = 1.4, " +
				     "hello = \"Hello World\" }");
			AssertPrint (thread, "s.hello", "(char *) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Struct: 5 - 7 - 1.4 - Hello World");
			AssertHitBreakpoint (thread, bpt_struct2, "test_struct_2", LineStruct2);

			AssertPrint (thread, "s",
				     "(TestStruct) { a = 5, b = 7, f = 1.4, " +
				     "hello = \"Hello World\" }");
			AssertPrint (thread, "s.hello", "(char *) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Struct: 5 - 7 - 1.4 - Hello World");
			AssertHitBreakpoint (thread, bpt_struct3, "test_struct_3", LineStruct3);

			AssertPrint (thread, "s.a", "(int) 5");
			AssertPrint (thread, "s.foo", "() { b = 800 }");
			AssertPrint (thread, "s.bar", "() { b = 9000 }");

			AssertExecute ("continue");
			AssertTargetOutput ("Test: 5 - 800,9000");
			AssertHitBreakpoint (thread, bpt_function_struct, "test_function_struct",
					     LineFunctionStruct);

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bitfield, "test_bitfield", LineBitField);

			AssertPrint (thread, "bitfield",
				     "(BitField) { a = 1, b = 3, c = 4, d = 9, e = 15 }");

			AssertExecute ("continue");
			AssertTargetOutput ("Bitfield: 3d307");
			AssertHitBreakpoint (thread, bpt_list, "test_list", LineList);

			AssertPrint (thread, "list.a", "(int) 9");
			AssertPrint (thread, "list.next->a", "(int) 9");

			AssertExecute ("continue");
			AssertTargetOutput ("9");

			AssertTargetExited (thread.Process);
		}
	}
}
