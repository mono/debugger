using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestManagedTypes : TestSuite
	{
		public TestManagedTypes ()
			: base ("TestManagedTypes")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", 218);

			AssertNoTargetOutput ();
			AssertNoDebuggerOutput ();

			const int line_simple = 120;
			const int line_boxed_value = 132;
			const int line_boxed_ref = 142;
			const int line_simple_array = 150;
			const int line_multi_value_array = 157;
			const int line_string_array = 164;
			const int line_multi_string_array = 172;
			const int line_struct_type = 179;
			const int line_class_type = 186;
			const int line_inherited_class_type = 195;
			const int line_complex_struct_type = 205;
			const int line_function_struct_type = 213;

			int bpt_simple = AssertBreakpoint (line_simple);
			int bpt_boxed_value = AssertBreakpoint (line_boxed_value);
			int bpt_boxed_ref = AssertBreakpoint (line_boxed_ref);
			int bpt_simple_array = AssertBreakpoint (line_simple_array);
			int bpt_multi_value_array = AssertBreakpoint (line_multi_value_array);
			int bpt_string_array = AssertBreakpoint (line_string_array);
			int bpt_multi_string_array = AssertBreakpoint (line_multi_string_array);
			int bpt_struct_type = AssertBreakpoint (line_struct_type);
			int bpt_class_type = AssertBreakpoint (line_class_type);
			int bpt_inherited_class_type = AssertBreakpoint (line_inherited_class_type);
			int bpt_complex_struct_type = AssertBreakpoint (line_complex_struct_type);
			int bpt_function_struct_type = AssertBreakpoint (line_function_struct_type);

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_simple, "X.Simple()", line_simple);

			AssertType (thread, "a", "System.Int32");
			AssertPrint (thread, "a", "(System.Int32) 5");
			AssertType (thread, "b", "System.Int64");
			AssertPrint (thread, "b", "(System.Int64) 7");
			AssertType (thread, "f", "System.Single");
			AssertPrint (thread, "f", "(System.Single) 0.7142857");
			AssertType (thread, "hello", "System.String");
			AssertPrint (thread, "hello", "(System.String) \"Hello World\"");

			AssertExecute ("set a = 9");
			AssertExecute ("set hello = \"Monkey\"");

			AssertPrint (thread, "a", "(System.Int32) 9");
			AssertPrint (thread, "hello", "(System.String) \"Monkey\"");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertTargetOutput ("7");
			AssertTargetOutput ("0.7142857");
			AssertTargetOutput ("Monkey");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_boxed_value, "X.BoxedValueType()",
					     line_boxed_value);

			AssertPrint (thread, "a", "(System.Int32) 5");
			AssertPrint (thread, "boxed_a", "(object) &(System.Int32) 5");
			AssertPrint (thread, "*boxed_a", "(System.Int32) 5");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_boxed_ref, "X.BoxedReferenceType()",
					     line_boxed_ref);

			AssertPrint (thread, "hello", "(System.String) \"Hello World\"");
			AssertPrint (thread, "boxed_hello", "(object) &(System.String) \"Hello World\"");
			AssertPrint (thread, "*boxed_hello", "(System.String) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_simple_array, "X.SimpleArray()",
					     line_simple_array);

			AssertPrint (thread, "a", "(System.Int32[]) [ 3, 4, 5 ]");
			AssertPrint (thread, "a[1]", "(System.Int32) 4");
			AssertExecute ("set a[2] = 9");
			AssertPrint (thread, "a[2]", "(System.Int32) 9");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_multi_value_array,
					     "X.MultiValueTypeArray()", line_multi_value_array);

			AssertPrint (thread, "a", "(System.Int32[,]) [ [ 6, 7, 8 ], [ 9, 10, 11 ] ]");
			AssertPrintException (thread, "a[1]",
					      "Index of array expression `a' out of bounds.");
			AssertPrint (thread, "a[1,2]", "(System.Int32) 11");
			AssertPrintException (thread, "a[2]",
					      "Index of array expression `a' out of bounds.");
			AssertExecute ("set a[1,2] = 50");

			AssertExecute ("continue");
			AssertTargetOutput ("50");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_string_array, "X.StringArray()",
					     line_string_array);

			AssertPrint (thread, "a", "(System.String[]) [ \"Hello\", \"World\" ]");
			AssertPrint (thread, "a[1]", "(System.String) \"World\"");
			AssertExecute ("set a[1] = \"Trier\"");
			AssertPrint (thread, "a", "(System.String[]) [ \"Hello\", \"Trier\" ]");
			AssertPrint (thread, "a[1]", "(System.String) \"Trier\"");

			AssertExecute ("continue");
			AssertTargetOutput ("System.String[]");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_multi_string_array, "X.MultiStringArray()",
					     line_multi_string_array);

			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Monkeys\" ] ]");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Monkeys\"");
			AssertExecute ("set a[2,1] = \"Primates\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Primates\"");
			AssertExecute ("set a[0,1] = \"Lions\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[0,1]", "(System.String) \"Lions\"");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Primates\"");

			AssertExecute ("set a[0,0] = \"Birds\"");
			AssertExecute ("set a[2,0] = \"Dogs\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Birds\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Dogs\", \"Primates\" ] ]");

			AssertExecute ("continue");
			AssertTargetOutput ("System.String[,]");
			AssertTargetOutput ("51.2");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_struct_type, "X.StructType()",
					     line_struct_type);

			AssertPrint (thread, "a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");

			AssertExecute ("continue");
			AssertTargetOutput ("A");
			AssertTargetOutput ("5");
			AssertTargetOutput ("3.14");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_class_type, "X.ClassType()",
					     line_class_type);

			AssertPrint (thread, "b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			AssertExecute ("continue");
			AssertTargetOutput ("B");
			AssertTargetOutput ("8");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_inherited_class_type,
					     "X.InheritedClassType()", line_inherited_class_type);

			AssertPrint (thread, "c",
				     "(C) { a = 8, f = 3.14 }");
			AssertPrint (thread, "b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");
			AssertPrint (thread, "(B) c",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_complex_struct_type,
					     "X.ComplexStructType()", line_complex_struct_type);

			AssertPrint (thread, "d.a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");
			AssertPrint (thread, "d.b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");
			AssertPrint (thread, "d.c",
				     "(C) { a = 8, f = 3.14 }");
			AssertPrint (thread, "d.s",
				     "(System.String[]) [ \"Eintracht Trier\" ]");

			AssertExecute ("continue");
			AssertTargetOutput ("Eintracht Trier");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_function_struct_type,
					     "X.FunctionStructType()", line_function_struct_type);

			AssertPrint (thread, "e", "(E) { a = 9 }");
			AssertPrint (thread, "e.a", "(System.Int32) 9");
			AssertPrint (thread, "e.Foo (5)", "(System.Int64) 5");

			AssertExecute ("kill");
		}
	}
}
