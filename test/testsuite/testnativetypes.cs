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

		[Test]
		[Category("Native")]
		[Category("NativeTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "main");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "simple", "simple");

			AssertPrint (thread, "a", "(int) 5");
			AssertPrint (thread, "b", "(long int) 7");
			AssertPrint (thread, "f", "(float) 0.7142857");
			AssertPrint (thread, "hello", "(char *) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Simple: 5 - 7 - 0.714286 - Hello World");
			AssertHitBreakpoint (thread, "struct", "test_struct");

			AssertPrint (thread, "s",
				     "(_TestStruct) { a = 5, b = 7, f = 1.4, " +
				     "hello = \"Hello World\" }");
			AssertPrint (thread, "s.hello", "(char *) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Struct: 5 - 7 - 1.4 - Hello World");
			AssertHitBreakpoint (thread, "struct2", "test_struct_2");

			AssertPrint (thread, "s",
				     "(TestStruct) { a = 5, b = 7, f = 1.4, " +
				     "hello = \"Hello World\" }");
			AssertPrint (thread, "s.hello", "(char *) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Struct: 5 - 7 - 1.4 - Hello World");
			AssertHitBreakpoint (thread, "struct3", "test_struct_3");

			AssertPrint (thread, "s.a", "(int) 5");
			AssertPrint (thread, "s.foo", "() { b = 800 }");
			AssertPrint (thread, "s.bar", "() { b = 9000 }");

			AssertExecute ("continue");
			AssertTargetOutput ("Test: 5 - 800,9000");
			AssertHitBreakpoint (thread, "function struct", "test_function_struct");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "bitfield", "test_bitfield");

			AssertPrint (thread, "bitfield",
				     "(BitField) { a = 1, b = 3, c = 4, d = 9, e = 15, f = 8 }");

			AssertExecute ("continue");
			AssertTargetOutput ("Bitfield: 3");
			AssertHitBreakpoint (thread, "list", "test_list");

			AssertPrint (thread, "list.a", "(int) 9");
			AssertPrint (thread, "list.next->a", "(int) 9");

			AssertExecute ("continue");
			AssertTargetOutput ("9");

			AssertHitBreakpoint (thread, "funcptr", "test_function_ptr");

			AssertType (thread, "func_ptr", "void (*) (int)");
			AssertType (thread, "func_ptr2", "typedef test_func_ptr = void (*) (int)");
			AssertPrintException (thread, "*func_ptr",
					     "Expression `func_ptr' is not a pointer.");
			AssertPrintException (thread, "*func_ptr2",
					      "Expression `func_ptr2' is not a pointer.");
			AssertTypeException (thread, "*func_ptr",
					     "Expression `func_ptr' is not a pointer.");
			AssertTypeException (thread, "*func_ptr2",
					     "Expression `func_ptr2' is not a pointer.");
			AssertType (thread, "func_ptr3", "test_func_ptr*");
			AssertType (thread, "*func_ptr3", "typedef test_func_ptr = void (*) (int)");
			AssertTypeException (thread, "**func_ptr3",
					     "Expression `*func_ptr3' is not a pointer.");

			AssertExecute ("continue");
			AssertTargetOutput ("Test: 3");
			AssertTargetOutput ("Test: 9");
			AssertTargetOutput ("Test: 11");

			AssertHitBreakpoint (thread, "array", "test_array");
			AssertPrint (thread, "array->simple_array", "(int []) [ 8192, 55, 71 ]");
			AssertPrint (thread, "array->multi_array",
				     "(long int []) [ [ 16, 32, 64 ], [ 24, 48, 96 ] ]");
			AssertPrint (thread, "array->anonymous_array", "(float []) [ ]");
			AssertPrint (thread, "array->anonymous_array [0]", "(float) 3.141593");
			AssertPrint (thread, "array->anonymous_array [1]", "(float) 2.718282");
			AssertExecute ("continue");

			AssertTargetExited (thread.Process);
		}
	}
}
